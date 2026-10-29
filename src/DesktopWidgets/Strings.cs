using System.Globalization;

namespace DesktopWidgets;

/// <summary>UI text in the language chosen in the tray menu (English or Ukrainian).</summary>
public static class Strings
{
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo Uk = CultureInfo.GetCultureInfo("uk-UA");

    /// <summary>The saved choice, or the Windows display language on first start.</summary>
    public static Language Current => SettingsStore.Current.Language
        ?? (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "uk" ? Language.Ukrainian : Language.English);

    /// <summary>Culture for dates and numbers, so the clock's day and month match the menus.</summary>
    public static CultureInfo Culture => Current == Language.Ukrainian ? Uk : En;

    /// <summary>Language code for the geocoding API (city names).</summary>
    public static string Code => Current == Language.Ukrainian ? "uk" : "en";

    private static string T(string en, string uk) => Current == Language.Ukrainian ? uk : en;

    // Tray menu
    public static string AddClock => T("Add clock", "Додати годинник");
    public static string AddWeather => T("Add weather", "Додати погоду");
    public static string Background => T("Background", "Фон");
    public static string FrostedBlur => T("Frosted blur", "Матове розмиття");
    public static string NoBlur => T("No blur", "Без розмиття");
    public static string BlurAmount(int percent) => T($"Blur amount: {percent}%", $"Розмиття: {percent}%");
    public static string RefreshBackground => T("Refresh background", "Оновити фон");
    public static string WidgetShadow => T("Widget shadow", "Тінь віджетів");
    public static string Theme => T("Theme", "Тема");
    public static string ThemeAuto => T("Auto", "Авто");
    public static string ThemeLight => T("Light (dark text)", "Світла (темний текст)");
    public static string ThemeDark => T("Dark (white text)", "Темна (білий текст)");
    public static string LanguageMenu => T("Language", "Мова");
    public static string WeatherLocationMenu => T("Weather location…", "Місто для погоди…");
    public static string UseFahrenheit => T("Use Fahrenheit", "Градуси Фаренгейта");
    public static string StartWithWindows => T("Start with Windows", "Запускати з Windows");
    public static string Exit => T("Exit", "Вийти");

    // Widget menu
    public static string RefreshWeather => T("Refresh weather", "Оновити погоду");
    public static string SetLocation => T("Set location…", "Вибрати місто…");
    public static string Scale(double scale) => T("Scale", "Масштаб") + " (" + scale.ToString("P0", Culture) + ")";
    public static string LockPosition => T("Lock position", "Закріпити позицію");
    public static string RemoveWidget => T("Remove widget", "Видалити віджет");

    // Location dialog
    public static string WeatherLocationTitle => T("Weather location", "Місто для погоди");
    public static string CityPrompt => T("City name (leave empty to detect by IP):", "Назва міста (залиште порожнім, щоб визначити за IP):");
    public static string AutoIp => T("Auto (IP)", "Авто (IP)");

    // Weather widget
    public static string Loading => T("Loading…", "Завантаження…");
    public static string NoData => T("No data", "Немає даних");
    public static string Updated(System.DateTime time) => T("Updated ", "Оновлено ") + time.ToString("t", Culture);
    public static string UpdateFailed(string error) => T("Update failed: ", "Не вдалося оновити: ") + error;
    public static string CityNotFound(string city) => T($"City \"{city}\" not found", $"Місто «{city}» не знайдено");
}
