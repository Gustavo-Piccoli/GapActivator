using GapActivator.App.Scheduling;

namespace GapActivator.App.Localization;

public sealed partial class Localizer
{
    public string ModeText(ScheduleMode mode) => mode switch
    {
        ScheduleMode.EveryMinutes => T("Every N minutes", "A cada N minutos"),
        ScheduleMode.EveryHours => T("Every N hours", "A cada N horas"),
        ScheduleMode.Daily => T("Daily", "Diariamente"),
        ScheduleMode.Weekly => T("Weekly", "Semanalmente"),
        ScheduleMode.Monthly => T("Monthly", "Mensalmente"),
        ScheduleMode.Once => T("Once", "Uma vez"),
        ScheduleMode.Advanced => T("Advanced (RRULE)", "Avançado (RRULE)"),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    public string PreviewHeading => T("Next 5 runs", "Próximas 5 execuções");

    public string PreviewNone => T("No upcoming run.", "Nenhuma execução futura.");

    public string NoWeekday => T("Choose at least one weekday.", "Escolha pelo menos um dia da semana.");

    public string MonthDayHint => T("Months without this day are skipped.", "Meses sem este dia são ignorados.");

    public string DailySummary(string time) => string.Format(Culture, T("Daily at {0}", "Diariamente às {0}"), time);

    public string MonthlySummary(string day, string time) =>
        string.Format(Culture, T("Monthly on day {0} at {1}", "Mensalmente no dia {0} às {1}"), day, time);

    public string OnceSummary(string at) => string.Format(Culture, T("Once on {0}", "Uma vez em {0}"), at);

    // Not in the Fixed strings: the other summaries, the form labels, the validation texts and the preview states.
    public string EveryMinutesSummary(int minutes) => minutes == 1
        ? T("Every minute", "A cada minuto")
        : string.Format(Culture, T("Every {0} minutes", "A cada {0} minutos"), minutes);

    public string EveryHoursSummary(int hours) => hours == 1
        ? T("Every hour", "A cada hora")
        : string.Format(Culture, T("Every {0} hours", "A cada {0} horas"), hours);

    public string WeeklySummary(string days, string time) =>
        string.Format(Culture, T("Weekly on {0} at {1}", "Semanalmente em {0} às {1}"), days, time);

    public string DayShort(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => T("Mon", "Seg"),
        DayOfWeek.Tuesday => T("Tue", "Ter"),
        DayOfWeek.Wednesday => T("Wed", "Qua"),
        DayOfWeek.Thursday => T("Thu", "Qui"),
        DayOfWeek.Friday => T("Fri", "Sex"),
        DayOfWeek.Saturday => T("Sat", "Sáb"),
        DayOfWeek.Sunday => T("Sun", "Dom"),
        _ => throw new ArgumentOutOfRangeException(nameof(day), day, null),
    };

    public string DayFull(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => T("Monday", "Segunda-feira"),
        DayOfWeek.Tuesday => T("Tuesday", "Terça-feira"),
        DayOfWeek.Wednesday => T("Wednesday", "Quarta-feira"),
        DayOfWeek.Thursday => T("Thursday", "Quinta-feira"),
        DayOfWeek.Friday => T("Friday", "Sexta-feira"),
        DayOfWeek.Saturday => T("Saturday", "Sábado"),
        DayOfWeek.Sunday => T("Sunday", "Domingo"),
        _ => throw new ArgumentOutOfRangeException(nameof(day), day, null),
    };

    public string FrequencyLabel => T("Frequency", "Frequência");

    public string EveryLabel => T("Repeat every", "Repetir a cada");

    public string MinutesUnit => T("minutes", "minutos");

    public string HoursUnit => T("hours", "horas");

    public string TimeLabel => T("Time of day", "Horário");

    public string WeekdaysLabel => T("Days of the week", "Dias da semana");

    public string DayOfMonthLabel => T("Day of the month", "Dia do mês");

    public string StartingLabel => T("Starting from", "A partir de");

    public string OnceLabel => T("Run on", "Executar em");

    public string DateLabel => T("Date", "Data");

    public string StartTimeLabel => T("Time", "Hora");

    public string RuleLabel => T("Recurrence rule", "Regra de recorrência");

    public string RuleHint => T(
        "Example: FREQ=DAILY;BYHOUR=8;BYMINUTE=0. Write numbers without a leading zero.",
        "Exemplo: FREQ=DAILY;BYHOUR=8;BYMINUTE=0. Escreva os números sem zero à esquerda.");

    public string RuleRequired => T("Enter a recurrence rule.", "Informe uma regra de recorrência.");

    public string DateRequired => T("Choose a date.", "Escolha uma data.");

    public string PreviewLoading => T("Calculating the next runs…", "Calculando as próximas execuções…");

    public string LocalTimeNote => T("Times use this computer's time zone.", "Os horários usam o fuso horário deste computador.");
}
