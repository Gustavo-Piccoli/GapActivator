using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using GapActivator.App.Localization;

namespace GapActivator.App.Tests.Localization;

public class LocalizerTests
{
    private static readonly string[] UnaccentedWords =
    [
        "nao", "voce", "execucao", "execucoes", "configuracao", "configuracoes", "servico", "servicos",
        "historico", "padrao", "proxima", "proximas", "ultima", "ultimo", "numero", "minimo", "maximo",
        "horario", "conexao", "atualizacao", "atualizacoes", "notificacao", "notificacoes", "codigo",
        "saida", "duracao", "tambem", "possivel", "sao",
    ];

    [Fact]
    public void SetLanguage_Portuguese_ReturnsPortugueseText()
    {
        var localizer = new Localizer(AppLanguage.English);
        localizer.NavProcesses.ShouldBe("Processes");
        localizer.NavHistory.ShouldBe("History");
        localizer.NavSettings.ShouldBe("Settings");

        localizer.SetLanguage(AppLanguage.Portuguese);

        localizer.NavProcesses.ShouldBe("Processos");
        localizer.NavHistory.ShouldBe("Histórico");
        localizer.NavSettings.ShouldBe("Configurações");
    }

    [Fact]
    public void SetLanguage_Changed_RaisesPropertyChangedWithEmptyName()
    {
        var localizer = new Localizer(AppLanguage.English);
        var names = new List<string?>();
        localizer.PropertyChanged += (_, e) => names.Add(e.PropertyName);

        localizer.SetLanguage(AppLanguage.Portuguese);

        names.ShouldBe([string.Empty]);
    }

    [Fact]
    public void SetLanguage_Same_DoesNotRaise()
    {
        var localizer = new Localizer(AppLanguage.English);
        var raised = false;
        localizer.PropertyChanged += (_, _) => raised = true;

        localizer.SetLanguage(AppLanguage.English);

        raised.ShouldBeFalse();
    }

    [Fact]
    public void Culture_FollowsTheLanguage()
    {
        var localizer = new Localizer(AppLanguage.English);
        localizer.Culture.Name.ShouldBe("en-US");

        localizer.SetLanguage(AppLanguage.Portuguese);

        localizer.Culture.Name.ShouldBe("pt-BR");
    }

    [Theory]
    [InlineData("pt-BR", AppLanguage.Portuguese)]
    [InlineData("pt-PT", AppLanguage.Portuguese)]
    [InlineData("fr-FR", AppLanguage.English)]
    [InlineData("en-US", AppLanguage.English)]
    public void DefaultLanguage_FollowsTheWindowsUiCulture(string culture, AppLanguage expected) =>
        Localizer.DefaultLanguage(CultureInfo.GetCultureInfo(culture)).ShouldBe(expected);

    [Fact]
    public void EveryText_HasBothLanguages()
    {
        var problems = new List<string>();

        foreach (var (name, nullable, evaluate) in Members())
        {
            var en = Evaluate(evaluate, AppLanguage.English);
            var pt = Evaluate(evaluate, AppLanguage.Portuguese);
            for (var i = 0; i < en.Count; i++)
            {
                var e = en[i];
                var p = pt[i];
                if (e is null || p is null)
                {
                    if (!(nullable && e is null && p is null))
                    {
                        problems.Add($"{name}: null result");
                    }

                    continue;
                }

                if (e.Length == 0 || p.Length == 0 || e != e.Trim() || p != p.Trim())
                {
                    problems.Add($"{name}: empty or padded text");
                }

                if (e == p && !Localizer.SameInBothLanguages.Contains(name))
                {
                    problems.Add($"{name}: en and pt are equal ({e})");
                }
            }
        }

        problems.ShouldBeEmpty();
    }

    [Fact]
    public void PortugueseTexts_HaveNoUnaccentedCommonWords()
    {
        var pattern = new Regex(@"\b(" + string.Join('|', UnaccentedWords) + @")\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var problems = new List<string>();

        foreach (var (name, _, evaluate) in Members())
        {
            foreach (var text in Evaluate(evaluate, AppLanguage.Portuguese))
            {
                if (text is not null && pattern.IsMatch(text))
                {
                    problems.Add($"{name}: {text}");
                }
            }
        }

        problems.ShouldBeEmpty();
    }

    [Fact]
    public void SameInBothLanguages_ListsOnlyRealMembers()
    {
        var members = typeof(Localizer).GetMembers(BindingFlags.Public | BindingFlags.Instance)
            .Select(m => m.Name).ToHashSet();

        Localizer.SameInBothLanguages.Where(n => !members.Contains(n)).ShouldBeEmpty();
    }

    private static List<string?> Evaluate(Func<Localizer, IEnumerable<string?>> evaluate, AppLanguage language) =>
        [.. evaluate(new Localizer(language))];

    private static List<(string Name, bool Nullable, Func<Localizer, IEnumerable<string?>> Evaluate)> Members()
    {
        var nullability = new NullabilityInfoContext();
        var list = new List<(string, bool, Func<Localizer, IEnumerable<string?>>)>();

        foreach (var property in typeof(Localizer).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(p => p.PropertyType == typeof(string) && p.GetIndexParameters().Length == 0))
        {
            list.Add((property.Name, false, l => [(string?)property.GetValue(l)]));
        }

        foreach (var method in typeof(Localizer).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                     .Where(m => m.ReturnType == typeof(string) && !m.IsSpecialName))
        {
            var nullable = nullability.Create(method.ReturnParameter).ReadState == NullabilityState.Nullable;
            var parameters = method.GetParameters();
            if (parameters.Length == 1 && parameters[0].ParameterType.IsEnum)
            {
                var values = Enum.GetValues(parameters[0].ParameterType).Cast<object>().ToArray();
                list.Add((method.Name, nullable, l => values.Select(v => (string?)method.Invoke(l, [v]))));
            }
            else
            {
                var args = parameters.Select(p => p.ParameterType == typeof(int) ? (object)2 : "x").ToArray();
                list.Add((method.Name, nullable, l => [(string?)method.Invoke(l, args)]));
            }
        }

        return list;
    }
}
