using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Storage;

namespace ClickerApp
{
    public partial class MainPage : ContentPage
    {
        private long coins = 0;
        private long clickPower = 1;
        private int autoClickers = 0;
        private long upgradeCost = 10;
        private long autoClickerCost = 50;

        private int critChanceLevel = 0;
        private long critUpgradeCost = 100;
        private const double BaseCritChance = 0.05;
        private const double CritChancePerLevel = 0.03;
        private const double MaxCritChance = 0.75;
        private const int CritMultiplier = 5;

        private int prestigeStars = 0;
        private double PrestigeBonus => 1 + prestigeStars * 0.10;

        private long totalCoinsEarned = 0;

        private readonly HashSet<string> unlockedAchievements = new();
        private IDispatcherTimer? autoClickTimer;
        private long pendingOfflineEarnings = 0;

        private static class Keys
        {
            public const string Coins = "coins";
            public const string ClickPower = "clickPower";
            public const string AutoClickers = "autoClickers";
            public const string UpgradeCost = "upgradeCost";
            public const string AutoClickerCost = "autoClickerCost";
            public const string CritChanceLevel = "critChanceLevel";
            public const string CritUpgradeCost = "critUpgradeCost";
            public const string PrestigeStars = "prestigeStars";
            public const string TotalCoinsEarned = "totalCoinsEarned";
            public const string Achievements = "achievements";
            public const string LastSaveTicks = "lastSaveTicks";
        }

        public MainPage()
        {
            InitializeComponent();

            LoadGame();
            ApplyOfflineEarnings();
            StartAutoClicker();
            UpdateUI();
        }

        private async void OnClickButtonClicked(object? sender, EventArgs e)
        {
            bool isCritical = Random.Shared.NextDouble() < CurrentCritChance;

            long baseGain = (long)Math.Round(clickPower * PrestigeBonus);
            if (baseGain < 1) baseGain = 1;
            long gained = isCritical ? baseGain * CritMultiplier : baseGain;

            coins += gained;
            totalCoinsEarned += gained;

            UpdateUI();
            CheckAchievements();
            SaveGame();

            await AnimateClick(isCritical, gained);
        }

        private double CurrentCritChance =>
            Math.Min(MaxCritChance, BaseCritChance + critChanceLevel * CritChancePerLevel);

        private async Task AnimateClick(bool isCritical, long gained)
        {
            ClickButton.Scale = 0.92;
            _ = ClickButton.ScaleTo(1.0, 90, Easing.CubicOut);

            FloatingLabel.TextColor = isCritical ? Color.FromArgb("#FF5252") : Color.FromArgb("#FFD700");
            FloatingLabel.Text = isCritical ? $"КРИТ! +{FormatNumber(gained)}" : $"+{FormatNumber(gained)}";
            FloatingLabel.Opacity = 1;
            FloatingLabel.TranslationY = 0;

            await Task.WhenAll(
                FloatingLabel.FadeTo(0, 600),
                FloatingLabel.TranslateTo(0, -40, 600, Easing.CubicOut));
        }

        private void OnUpgradeClicked(object? sender, EventArgs e)
        {
            if (coins < upgradeCost) return;

            coins -= upgradeCost;
            clickPower++;
            upgradeCost = (long)(upgradeCost * 1.6);

            UpdateUI();
            CheckAchievements();
            SaveGame();
        }

        private void OnBuyAutoClickerClicked(object? sender, EventArgs e)
        {
            if (coins < autoClickerCost) return;

            coins -= autoClickerCost;
            autoClickers++;
            autoClickerCost = (long)(autoClickerCost * 1.7);

            UpdateUI();
            CheckAchievements();
            SaveGame();
        }

        private void OnBuyCritChanceClicked(object? sender, EventArgs e)
        {
            if (coins < critUpgradeCost || CurrentCritChance >= MaxCritChance) return;

            coins -= critUpgradeCost;
            critChanceLevel++;
            critUpgradeCost = (long)(critUpgradeCost * 1.8);

            UpdateUI();
            SaveGame();
        }

        private async void OnPrestigeClicked(object? sender, EventArgs e)
        {
            const long minToPrestige = 1000;

            if (coins < minToPrestige)
            {
                await DisplayAlert(
                    "Престиж",
                    $"Потрібно накопичити щонайменше {FormatNumber(minToPrestige)} монет, щоб отримати першу зірку.",
                    "Гаразд");
                return;
            }

            int newStars = (int)Math.Floor(Math.Sqrt(coins / (double)minToPrestige));
            bool confirmed = await DisplayAlert(
                "Престиж",
                $"Ви обміняєте весь поточний прогрес на {newStars} ★ (кожна зірка дає +10% до доходу назавжди).\n\n" +
                "Монети, покращення кліку та автоклікери буде скинуто. Продовжити?",
                "Так, престиж!", "Скасувати");

            if (!confirmed) return;

            prestigeStars += newStars;
            coins = 0;
            clickPower = 1;
            autoClickers = 0;
            upgradeCost = 10;
            autoClickerCost = 50;

            UpdateUI();
            CheckAchievements();
            SaveGame();
        }

        private void StartAutoClicker()
        {
            autoClickTimer = Dispatcher.CreateTimer();
            autoClickTimer.Interval = TimeSpan.FromSeconds(1);
            autoClickTimer.Tick += (s, e) =>
            {
                if (autoClickers <= 0) return;

                long income = (long)Math.Round(autoClickers * PrestigeBonus);
                if (income < 1) income = 1;

                coins += income;
                totalCoinsEarned += income;

                UpdateUI();
                CheckAchievements();
            };
            autoClickTimer.Start();
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            SaveGame();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            if (pendingOfflineEarnings > 0)
            {
                long earned = pendingOfflineEarnings;
                pendingOfflineEarnings = 0;
                await DisplayAlert(
                    "З поверненням!",
                    $"Поки вас не було, автоклікери заробили {FormatNumber(earned)} монет.",
                    "Клас!");
            }
        }

        private void ApplyOfflineEarnings()
        {
            long lastTicks = Preferences.Get(Keys.LastSaveTicks, 0L);
            if (lastTicks == 0 || autoClickers <= 0) return;

            var last = new DateTime(lastTicks, DateTimeKind.Utc);
            var elapsed = DateTime.UtcNow - last;

            var cappedSeconds = Math.Min(elapsed.TotalSeconds, 8 * 60 * 60);
            if (cappedSeconds < 5) return;

            long earned = (long)Math.Round(autoClickers * PrestigeBonus * cappedSeconds);
            if (earned <= 0) return;

            coins += earned;
            totalCoinsEarned += earned;
            pendingOfflineEarnings = earned;
        }

        private void SaveGame()
        {
            Preferences.Set(Keys.Coins, coins);
            Preferences.Set(Keys.ClickPower, clickPower);
            Preferences.Set(Keys.AutoClickers, autoClickers);
            Preferences.Set(Keys.UpgradeCost, upgradeCost);
            Preferences.Set(Keys.AutoClickerCost, autoClickerCost);
            Preferences.Set(Keys.CritChanceLevel, critChanceLevel);
            Preferences.Set(Keys.CritUpgradeCost, critUpgradeCost);
            Preferences.Set(Keys.PrestigeStars, prestigeStars);
            Preferences.Set(Keys.TotalCoinsEarned, totalCoinsEarned);
            Preferences.Set(Keys.Achievements, string.Join(',', unlockedAchievements));
            Preferences.Set(Keys.LastSaveTicks, DateTime.UtcNow.Ticks);
        }

        private void LoadGame()
        {
            coins = Preferences.Get(Keys.Coins, 0L);
            clickPower = Preferences.Get(Keys.ClickPower, 1L);
            autoClickers = Preferences.Get(Keys.AutoClickers, 0);
            upgradeCost = Preferences.Get(Keys.UpgradeCost, 10L);
            autoClickerCost = Preferences.Get(Keys.AutoClickerCost, 50L);
            critChanceLevel = Preferences.Get(Keys.CritChanceLevel, 0);
            critUpgradeCost = Preferences.Get(Keys.CritUpgradeCost, 100L);
            prestigeStars = Preferences.Get(Keys.PrestigeStars, 0);
            totalCoinsEarned = Preferences.Get(Keys.TotalCoinsEarned, 0L);

            var savedAchievements = Preferences.Get(Keys.Achievements, string.Empty);
            if (!string.IsNullOrEmpty(savedAchievements))
            {
                foreach (var id in savedAchievements.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    unlockedAchievements.Add(id);
            }
        }

        private record Achievement(string Id, string Title, Func<MainPage, bool> Condition);

        private static readonly List<Achievement> AllAchievements = new()
        {
            new("coins_100",    "Перші 100 монет",         p => p.coins >= 100),
            new("coins_1000",   "Тисячник",                p => p.coins >= 1_000),
            new("coins_100000", "Магнат: 100 000 монет",   p => p.coins >= 100_000),
            new("power_10",     "Сила кліку: 10",          p => p.clickPower >= 10),
            new("power_50",     "Сила кліку: 50",          p => p.clickPower >= 50),
            new("auto_1",       "Перший автоклікер",       p => p.autoClickers >= 1),
            new("auto_10",      "Автоматизація: 10",       p => p.autoClickers >= 10),
            new("prestige_1",   "Перша зірка престижу",    p => p.prestigeStars >= 1),
            new("earned_1m",    "Зароблено мільйон монет", p => p.totalCoinsEarned >= 1_000_000),
        };

        private void CheckAchievements()
        {
            foreach (var achievement in AllAchievements)
            {
                if (unlockedAchievements.Contains(achievement.Id)) continue;
                if (!achievement.Condition(this)) continue;

                unlockedAchievements.Add(achievement.Id);
                _ = ShowAchievementToast(achievement.Title);
            }
        }

        private async Task ShowAchievementToast(string text)
        {
            AchievementLabel.Text = text;
            AchievementBadge.Opacity = 0;
            AchievementBadge.IsVisible = true;

            await AchievementBadge.FadeTo(1, 200);
            await Task.Delay(1800);
            await AchievementBadge.FadeTo(0, 400);
            AchievementBadge.IsVisible = false;
        }

        private void UpdateUI()
        {
            CoinsLabel.Text = $"Монети: {FormatNumber(coins)}";
            ClickPowerLabel.Text = $"За клік: {FormatNumber((long)Math.Round(clickPower * PrestigeBonus))}";
            AutoClickerLabel.Text =
                $"Автоклікери: {autoClickers} (+{FormatNumber((long)Math.Round(autoClickers * PrestigeBonus))}/с)";
            CritChanceLabel.Text = $"Шанс криту: {CurrentCritChance:P0} (x{CritMultiplier})";
            PrestigeLabel.Text = prestigeStars > 0
                ? $"Престиж: {prestigeStars} ★ (+{prestigeStars * 10}% до доходу)"
                : "Престиж: 0 ★";

            UpgradeButton.Text = $"Покращити клік — {FormatNumber(upgradeCost)} монет";
            UpgradeButton.IsEnabled = coins >= upgradeCost;

            AutoClickerButton.Text = $"Купити автоклікер — {FormatNumber(autoClickerCost)} монет";
            AutoClickerButton.IsEnabled = coins >= autoClickerCost;

            CritChanceButton.Text = CurrentCritChance >= MaxCritChance
                ? "Шанс криту прокачано до максимуму"
                : $"Прокачати крит — {FormatNumber(critUpgradeCost)} монет";
            CritChanceButton.IsEnabled = coins >= critUpgradeCost && CurrentCritChance < MaxCritChance;

            PrestigeButton.Text = "Престиж (мін. 1 000 монет)";
            PrestigeButton.IsEnabled = coins >= 1000;
        }

        private static string FormatNumber(long value)
        {
            double v = value;
            string[] suffixes = { "", " тис.", " млн", " млрд", " трлн", " квдрлн" };
            int i = 0;
            while (v >= 1000 && i < suffixes.Length - 1)
            {
                v /= 1000;
                i++;
            }

            return i == 0 ? value.ToString("N0") : $"{v:0.##}{suffixes[i]}";
        }
    }
}
