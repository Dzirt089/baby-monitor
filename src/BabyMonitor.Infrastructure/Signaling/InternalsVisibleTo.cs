using System.Runtime.CompilerServices;

// Contract/security-тесты Infrastructure проверяют внутренние элементы протокола (кодек кадров,
// binary-формат hello, транскрипты assertion), не раскрывая их в публичный API адаптера.
[assembly: InternalsVisibleTo("BabyMonitor.Infrastructure.Tests")]
