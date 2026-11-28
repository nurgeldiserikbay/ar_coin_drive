using UnityEngine;

/// <summary>
/// The player's garage: coin balance, owned cars and each car's upgrades and look. Saved in
/// PlayerPrefs as one compact string with a checksum, so a hand-edited save resets instead of
/// granting free coins. Prices and rewards live here too.
/// </summary>
public static class PlayerProfile
{
    private const string Key = "ARCoinDrive.Profile";
    private const string Salt = "acd-garage-7f3c";
    private const int StartingCoins = 300;

    // Car prices by catalogue index: the RC buggy and the tuner are free.
    // -1 marks a premium car, sold for real money in the store (GameStore).
    private static readonly int[] CarPrices = { 0, 0, 1800, 4500, 2600, 3400, -1, -1 };
    private const int VipBit = 30;
    private static readonly int[] EngineCost = { 250, 500, 900 };
    private static readonly int[] TyreCost = { 180, 360, 700 };
    private static readonly int[] WeightCost = { 200, 420, 800 };
    public static readonly int[] FinishCost = { 0, 150, 200 };
    public static readonly int[] RimCost = { 0, 250, 300, 350 };
    public static readonly int[] SpoilerCost = { 0, 0, 400 };
    public const int StripeCost = 150;

    // Rewards.
    // Prizes are scaled by the rival level (GameSettings.DifficultyPrize). Coins are slow to earn on
    // purpose: rewarded ads (AdRewards) are the quick way to upgrades and repairs.
    private static readonly int[] RacePlaceReward = { 120, 70, 40, 25 };
    private static readonly int[] BattlePlaceReward = { 110, 60, 35, 20 };
    public const int CoinPickupFree = 1;
    public const int CoinPickupRound = 1;

    private static bool _loaded;
    private static int _coins;
    private static int _owned;
    private static CarTune[] _tunes;
    private static CarLook[] _looks;
    private static int[] _unlocked; // per car: bought cosmetic options (bit flags)
    private static int[] _wear;     // per car: 0 = like new, 100 = worn out (see Condition)

    public static event System.Action Changed;

    public static int Coins { get { Load(); return _coins; } }

    public static bool Owns(int car) { Load(); return (_owned & (1 << car)) != 0; }
    public static bool IsPremium(int car) => CarPrice(car) < 0;

    /// <summary>VIP pass: double coins from every reward.</summary>
    public static bool Vip { get { Load(); return (_owned & (1 << VipBit)) != 0; } }

    /// <summary>Grants a store purchase (premium car or VIP). Safe to call again for the same item.</summary>
    public static void GrantCar(int car)
    {
        Load();
        if (Owns(car)) return;
        _owned |= 1 << Clamp(car);
        Save();
    }

    public static void GrantVip()
    {
        Load();
        if (Vip) return;
        _owned |= 1 << VipBit;
        Save();
    }
    public static int CarPrice(int car) => CarPrices[Mathf.Clamp(car, 0, CarPrices.Length - 1)];
    public static CarTune Tune(int car) { Load(); return _tunes[Clamp(car)]; }
    public static CarLook Look(int car) { Load(); return _looks[Clamp(car)]; }

    /// <summary>The car's stats with its upgrades.</summary>
    public static CarSpec Spec(int car) => CarTuning.Worn(CarTuning.Apply(Clamp(car), Tune(car)), Condition(car));

    public static bool TryBuyCar(int car)
    {
        Load();
        if (Owns(car) || IsPremium(car) || !Spend(CarPrice(car))) return false;
        _owned |= 1 << car;
        Save();
        return true;
    }

    // ------------------------------------------------------------------ upgrades

    public enum Upgrade { Engine, Tyres, Weight }

    public static int Level(int car, Upgrade upgrade)
    {
        var tune = Tune(car);
        return upgrade == Upgrade.Engine ? tune.Engine : upgrade == Upgrade.Tyres ? tune.Tyres : tune.Weight;
    }

    /// <summary>Cost of the next level, or 0 when maxed.</summary>
    public static int UpgradeCost(int car, Upgrade upgrade)
    {
        var level = Level(car, upgrade);
        if (level >= CarTune.MaxLevel) return 0;
        var table = upgrade == Upgrade.Engine ? EngineCost : upgrade == Upgrade.Tyres ? TyreCost : WeightCost;
        // Faster cars cost more to tune.
        var scale = 1f + (IsPremium(car) ? 6000 : CarPrice(car)) / 9000f;
        return Mathf.RoundToInt(table[level] * scale / 10f) * 10;
    }

    public static bool TryUpgrade(int car, Upgrade upgrade)
    {
        Load();
        car = Clamp(car);
        if (!Owns(car)) return false;
        var cost = UpgradeCost(car, upgrade);
        if (cost <= 0 || !Spend(cost)) return false;
        var tune = _tunes[car];
        if (upgrade == Upgrade.Engine) tune.Engine++;
        else if (upgrade == Upgrade.Tyres) tune.Tyres++;
        else tune.Weight++;
        _tunes[car] = tune;
        Save();
        return true;
    }

    // ------------------------------------------------------------------ looks

    public enum Option { Finish, Rim, Spoiler, Stripes }

    // Unlock bits: finish 0-2, rims 3-6, spoilers 7-9, stripes 10. Stock options are always owned.
    private static int Bit(Option option, int value) =>
        option == Option.Finish ? value : option == Option.Rim ? 3 + value : option == Option.Spoiler ? 7 + value : 10;

    public static int OptionCost(Option option, int value) =>
        option == Option.Finish ? FinishCost[value] : option == Option.Rim ? RimCost[value]
        : option == Option.Spoiler ? SpoilerCost[value] : value != 0 ? StripeCost : 0;

    public static bool Unlocked(int car, Option option, int value)
    {
        Load();
        return OptionCost(option, value) == 0 || (_unlocked[Clamp(car)] & (1 << Bit(option, value))) != 0;
    }

    /// <summary>Applies a look option, buying it first if needed. Paint is always free.</summary>
    public static bool TrySetOption(int car, Option option, int value)
    {
        Load();
        car = Clamp(car);
        if (!Owns(car)) return false;
        if (!Unlocked(car, option, value))
        {
            if (!Spend(OptionCost(option, value))) return false;
            _unlocked[car] |= 1 << Bit(option, value);
        }

        var look = _looks[car];
        if (option == Option.Finish) look.Finish = value;
        else if (option == Option.Rim) look.Rim = value;
        else if (option == Option.Spoiler) look.Spoiler = value;
        else look.Stripes = value != 0;
        _looks[car] = look;
        Save();
        return true;
    }

    public static void SetPaint(int car, int paint)
    {
        Load();
        car = Clamp(car);
        var look = _looks[car];
        look.Paint = Mathf.Clamp(paint, 0, GameSettings.CarColors.Length - 1);
        _looks[car] = look;
        Save();
    }

    // ------------------------------------------------------------------ condition and repair

    /// <summary>0..100 %: crashes, battle hits and races wear a car down; a worn car is slower.</summary>
    public static int Condition(int car) { Load(); return 100 - _wear[Clamp(car)]; }

    /// <summary>Adds wear (percentage points) to a car after a round.</summary>
    public static void AddWear(int car, float points)
    {
        if (points < 0.5f) return;
        Load();
        car = Clamp(car);
        _wear[car] = Mathf.Clamp(_wear[car] + Mathf.RoundToInt(points), 0, 100);
        Save();
    }

    /// <summary>Full repair price: dearer cars cost more to fix.</summary>
    public static int RepairCost(int car)
    {
        Load();
        car = Clamp(car);
        if (_wear[car] <= 0) return 0;
        var value = IsPremium(car) ? 6000 : CarPrice(car);
        var perPoint = 1.5f + value / 2000f;
        return Mathf.Max(5, Mathf.CeilToInt(_wear[car] * perPoint / 5f) * 5);
    }

    public static bool TryRepair(int car)
    {
        Load();
        car = Clamp(car);
        var cost = RepairCost(car);
        if (cost <= 0 || !Spend(cost)) return false;
        _wear[car] = 0;
        Save();
        return true;
    }

    // ------------------------------------------------------------------ rewards

    public static int RaceReward(int place, int cars) => PlaceReward(RacePlaceReward, place, cars);
    public static int BattleReward(int place, int cars) => PlaceReward(BattlePlaceReward, place, cars);

    private static int PlaceReward(int[] table, int place, int cars)
    {
        if (place <= 0) return 0;
        // Last place always gets the smallest prize, so a 2-car race pays 1st or 4th-tier money.
        var index = place >= cars ? table.Length - 1 : Mathf.Min(place - 1, table.Length - 1);
        return Mathf.RoundToInt(table[index] * GameSettings.DifficultyPrize[GameSettings.Difficulty] / 5f) * 5;
    }

    /// <summary>Adds a reward (doubled with the VIP pass); returns what was actually added.</summary>
    public static int Reward(int amount)
    {
        if (amount <= 0) return 0;
        if (Vip) amount *= 2;
        Earn(amount);
        return amount;
    }

    public static void Earn(int amount)
    {
        if (amount <= 0) return;
        Load();
        _coins = Mathf.Min(_coins + amount, 9999999);
        Save();
    }

    private static bool Spend(int amount)
    {
        if (amount < 0 || _coins < amount) return false;
        _coins -= amount;
        return true;
    }

    // ------------------------------------------------------------------ network build code

    /// <summary>Upgrades and look (without paint) in 13 bits, for the network.</summary>
    public static int PackBuild(CarTune tune, CarLook look) =>
        (tune.Engine & 3) | (tune.Tyres & 3) << 2 | (tune.Weight & 3) << 4 | (look.Finish & 3) << 6
        | (look.Rim & 3) << 8 | (look.Spoiler & 3) << 10 | (look.Stripes ? 1 : 0) << 12;

    public static int BuildOf(int car) => PackBuild(Tune(car), Look(car)) | WearLevel(car) << 13;

    // Wear in 3 bits (0 = like new) above the 13 build bits: the build travels as 16 bits over LAN.
    private static int WearLevel(int car) { Load(); return Mathf.RoundToInt(_wear[Clamp(car)] * 7f / 100f); }

    /// <summary>Condition 0..100 carried in a build code (100 when it has no wear bits).</summary>
    public static int ConditionOf(int build) => 100 - Mathf.RoundToInt((build >> 13 & 7) * 100f / 7f);

    public static void UnpackBuild(int code, int paint, out CarTune tune, out CarLook look)
    {
        tune = new CarTune
        {
            Engine = Mathf.Min(code & 3, CarTune.MaxLevel),
            Tyres = Mathf.Min(code >> 2 & 3, CarTune.MaxLevel),
            Weight = Mathf.Min(code >> 4 & 3, CarTune.MaxLevel)
        };
        look = new CarLook
        {
            Paint = paint,
            Finish = Mathf.Min(code >> 6 & 3, CarLook.FinishNames.Length - 1),
            Rim = Mathf.Min(code >> 8 & 3, CarLook.RimNames.Length - 1),
            Spoiler = Mathf.Min(code >> 10 & 3, CarLook.SpoilerNames.Length - 1),
            Stripes = (code >> 12 & 1) != 0
        };
    }

    // ------------------------------------------------------------------ save / load

    private static int Clamp(int car) => Mathf.Clamp(car, 0, CarCatalog.All.Length - 1);

    private static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        var count = CarCatalog.All.Length;
        _tunes = new CarTune[count];
        _looks = new CarLook[count];
        _unlocked = new int[count];
        _wear = new int[count];
        for (var i = 0; i < count; i++) _looks[i] = CarLook.Stock(CarCatalog.Get(i).DefaultColor);
        _coins = StartingCoins;
        _owned = 0;
        for (var i = 0; i < count; i++) if (CarPrice(i) == 0) _owned |= 1 << i;

        if (!TryParse(PlayerPrefs.GetString(Key, ""))) Save();
        // The selected car must be one the player owns.
        if (!Owns(GameSettings.CarType)) GameSettings.CarType = 0;
    }

    private static bool TryParse(string data)
    {
        if (string.IsNullOrEmpty(data)) return false;
        var bar = data.LastIndexOf('|');
        if (bar < 0 || data.Substring(bar + 1) != Hash(data.Substring(0, bar))) return false;
        var parts = data.Substring(0, bar).Split(';');
        if (parts.Length < 3 || parts[0] != "1") return false;
        if (!int.TryParse(parts[1], out var coins) || !int.TryParse(parts[2], out var owned)) return false;
        _coins = Mathf.Max(0, coins);
        _owned |= owned;
        for (var i = 3; i < parts.Length; i++)
        {
            var f = parts[i].Split(',');
            if (f.Length < 9 || !int.TryParse(f[0], out var car) || car < 0 || car >= _tunes.Length) continue;
            int.TryParse(f[1], out var code);
            int.TryParse(f[2], out var paint);
            int.TryParse(f[3], out var unlocked);
            UnpackBuild(code, Mathf.Clamp(paint, 0, GameSettings.CarColors.Length - 1), out _tunes[car], out _looks[car]);
            _unlocked[car] = unlocked;
            if (f.Length > 4 && int.TryParse(f[4], out var wear)) _wear[car] = Mathf.Clamp(wear, 0, 100);
        }

        return true;
    }

    private static void Save()
    {
        var builder = new System.Text.StringBuilder();
        builder.Append("1;").Append(_coins).Append(';').Append(_owned);
        for (var i = 0; i < _tunes.Length; i++)
        {
            // Trailing fields are reserved for later options.
            builder.Append(';').Append(i).Append(',').Append(PackBuild(_tunes[i], _looks[i])).Append(',')
                .Append(_looks[i].Paint).Append(',').Append(_unlocked[i]).Append(',').Append(_wear[i]).Append(",0,0,0,0");
        }

        var body = builder.ToString();
        PlayerPrefs.SetString(Key, body + "|" + Hash(body));
        PlayerPrefs.Save();
        Changed?.Invoke();
    }

    private static string Hash(string text)
    {
        // FNV-1a over the salted text: enough to make casual save edits pointless.
        var hash = 2166136261u;
        foreach (var c in Salt + text)
        {
            hash ^= c;
            hash *= 16777619u;
        }

        return hash.ToString("x8");
    }

#if UNITY_EDITOR
    /// <summary>Editor tests: wipe the save.</summary>
    public static void ResetForTests(int coins)
    {
        PlayerPrefs.DeleteKey(Key);
        _loaded = false;
        Load();
        _coins = coins;
        Save();
    }
#endif
}
