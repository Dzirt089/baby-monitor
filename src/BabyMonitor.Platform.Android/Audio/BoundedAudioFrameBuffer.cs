using System.Threading.Channels;

namespace BabyMonitor.Platform.Android.Audio;

/// <summary>
/// Bounded-очередь PCM-кадров между фоновой задачей адаптера и вызывающим кодом
/// (Application-порт), с пулом заранее выделенных буферов и стратегией drop-oldest.
/// </summary>
/// <remarks>
/// В установившемся режиме кадр не требует новой аллокации: буфер берётся из пула, копируется и
/// возвращается в пул после чтения/записи. При перегрузке самый старый кадр отбрасывается, поэтому
/// задержка и память не растут безгранично (AGENTS.md §7: unbounded real-time buffers запрещены).
/// Очередь рассчитана на одного производителя и одного потребителя; каналы потокобезопасны, а
/// протокол «взять кадр через <see cref="TakeAsync"/> → вернуть буфер через
/// <see cref="FrameLease.Release"/>» должен соблюдаться.
/// </remarks>
internal sealed class BoundedAudioFrameBuffer
{
    private readonly Channel<byte[]> _free;
    private readonly Channel<Frame> _ready;
    private readonly int _capacityFrames;
    private int _completed;

    /// <summary>Создаёт очередь с ограниченным числом кадров.</summary>
    /// <param name="capacityFrames">Максимум кадров в очереди; не меньше одного.</param>
    /// <param name="frameBytes">Размер одного кадра в байтах; не меньше одного.</param>
    /// <exception cref="ArgumentOutOfRangeException">Значение вне допустимого диапазона.</exception>
    internal BoundedAudioFrameBuffer(int capacityFrames, int frameBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacityFrames);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameBytes);

        _capacityFrames = capacityFrames;
        FrameBytes = frameBytes;
        _ready = Channel.CreateBounded<Frame>(new BoundedChannelOptions(capacityFrames)
        {
            // Переполнение обрабатывается вручную: отброшенный кадр нужно вернуть в пул буферов,
            // поэтому FullMode.Wait + явное отбрасывание, а не DropOldest.
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        });
        _free = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false
        });

        for (var index = 0; index < capacityFrames; index++)
        {
            _free.Writer.TryWrite(new byte[frameBytes]);
        }
    }

    /// <summary>Размер одного кадра в байтах.</summary>
    internal int FrameBytes { get; }

    /// <summary>
    /// Копирует кадр в буфер из пула и ставит его в очередь. Если очередь заполнена, самый старый
    /// кадр отбрасывается (drop-oldest), при этом ожидание не блокирует вызывающий поток.
    /// </summary>
    /// <param name="frame">PCM-кадр не длиннее <see cref="FrameBytes"/>.</param>
    /// <param name="cancellationToken">Отмена ожидания места в очереди.</param>
    /// <returns><see langword="false"/>, если очередь остановлена (Stop/Dispose).</returns>
    /// <exception cref="ArgumentException">Кадр пуст или длиннее <see cref="FrameBytes"/>.</exception>
    /// <exception cref="OperationCanceledException">Ожидание отменено.</exception>
    internal async ValueTask<bool> EnqueueAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken)
    {
        if (frame.IsEmpty)
        {
            throw new ArgumentException("Кадр пуст.", nameof(frame));
        }

        if (frame.Length > FrameBytes)
        {
            throw new ArgumentException("Кадр больше размера буфера очереди.", nameof(frame));
        }

        if (Volatile.Read(ref _completed) == 1)
        {
            return false;
        }

        var buffer = RentBuffer();
        frame.Span.CopyTo(buffer);

        // Drop-oldest: ограниченная очередь, старая задержка не накапливается.
        while (_ready.Reader.Count >= _capacityFrames)
        {
            if (_ready.Reader.TryRead(out var dropped))
            {
                ReturnBuffer(dropped.Buffer);
            }
            else
            {
                break;
            }
        }

        try
        {
            await _ready.Writer.WriteAsync(new Frame(buffer, frame.Length), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            ReturnBuffer(buffer);
            return false;
        }
        catch (OperationCanceledException)
        {
            ReturnBuffer(buffer);
            throw;
        }

        return true;
    }

    /// <summary>
    /// Асинхронно берёт самый старый кадр. Возвращённый буфер обязан быть освобождён через
    /// <see cref="FrameLease.Release"/> после копирования/записи.
    /// </summary>
    /// <param name="cancellationToken">Отмена ожидания кадра.</param>
    /// <returns>Аренда кадра либо <see langword="null"/>, если очередь остановлена.</returns>
    /// <exception cref="OperationCanceledException">Ожидание отменено.</exception>
    internal async ValueTask<FrameLease?> TakeAsync(CancellationToken cancellationToken)
    {
        try
        {
            var frame = await _ready.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            return new FrameLease(this, frame);
        }
        catch (ChannelClosedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Завершает очередь: новые кадры не принимаются, ожидающие взятия получают
    /// <see langword="null"/>. Идемпотентно.
    /// </summary>
    internal void Complete()
    {
        if (Interlocked.Exchange(ref _completed, 1) == 1)
        {
            return;
        }

        _ready.Writer.TryComplete();
    }

    private void ReturnBuffer(byte[] buffer) => _free.Writer.TryWrite(buffer);

    private byte[] RentBuffer()
    {
        if (_free.Reader.TryRead(out var buffer))
        {
            return buffer;
        }

        // Потребитель ещё держит буфер в руках: выделяем не больше одного буфера сверх пула,
        // дальше буферы возвращаются в пул через FrameLease.Release.
        return new byte[FrameBytes];
    }

    internal readonly record struct Frame(byte[] Buffer, int Length);

    /// <summary>Аренда кадра из очереди: буфер живёт до <see cref="Release"/>.</summary>
    internal readonly struct FrameLease
    {
        private readonly BoundedAudioFrameBuffer _owner;
        private readonly byte[] _buffer;

        internal FrameLease(BoundedAudioFrameBuffer owner, Frame frame)
        {
            _owner = owner;
            _buffer = frame.Buffer;
            Length = frame.Length;
        }

        /// <summary>Длина кадра в байтах.</summary>
        internal int Length { get; }

        /// <summary>Буфер кадра для копирования в выходной поток (не удерживать после Release).</summary>
        internal byte[] Buffer => _buffer;

        /// <summary>Копирует кадр в предоставленный буфер вызывающего.</summary>
        /// <param name="destination">Буфер не меньше <see cref="Length"/>.</param>
        internal void CopyTo(Span<byte> destination) => _buffer.AsSpan(0, Length).CopyTo(destination);

        /// <summary>Возвращает буфер в пул очереди. Повторный вызов не предусмотрен.</summary>
        internal void Release()
        {
            if (_owner is not null)
            {
                _owner.ReturnBuffer(_buffer);
            }
        }
    }
}
