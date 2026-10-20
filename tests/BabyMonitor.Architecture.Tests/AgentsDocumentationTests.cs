namespace BabyMonitor.Architecture.Tests;

/// <summary>AGENTS.md — обязательный источник правил в корне репозитория.</summary>
public sealed class AgentsDocumentationTests
{
    [Fact]
    public void AGENTS_md_находится_в_корне_репозитория()
    {
        var agentsPath = ArchitectureTestSupport.GetPathFromRoot("AGENTS.md");

        Assert.True(File.Exists(agentsPath), $"В корне репозитория отсутствует обязательный файл AGENTS.md: {agentsPath}.");
    }

    [Theory]
    [InlineData("## 5. Архитектура")]
    [InlineData("## 15. Тестирование")]
    [InlineData("## 20. Безусловно запрещено")]
    [InlineData("## 21. Definition of Done")]
    public void AGENTS_md_содержит_маркер_ключевого_раздела(string marker)
    {
        var agentsPath = ArchitectureTestSupport.GetPathFromRoot("AGENTS.md");
        Assert.True(File.Exists(agentsPath), $"В корне репозитория отсутствует обязательный файл AGENTS.md: {agentsPath}.");

        var content = File.ReadAllText(agentsPath);

        Assert.True(
            content.Contains(marker, StringComparison.Ordinal),
            $"В AGENTS.md ({agentsPath}) отсутствует маркер ключевого раздела '{marker}'.");
    }
}
