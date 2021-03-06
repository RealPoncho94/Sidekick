using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Serilog;
using Sidekick.Business.Apis.Poe.Models;
using Sidekick.Business.Apis.Poe.Parser;
using Sidekick.Business.Apis.Poe.Trade;
using Sidekick.Business.Apis.Poe.Trade.Data.Items;
using Sidekick.Business.Apis.Poe.Trade.Data.Static;
using Sidekick.Business.Apis.Poe.Trade.Data.Stats;
using Sidekick.Business.Apis.Poe.Trade.Search;
using Sidekick.Business.Apis.Poe.Trade.Search.Filters;
using Sidekick.Business.Apis.PoeNinja;
using Sidekick.Business.Apis.PoePriceInfo.Models;
using Sidekick.Business.ItemCategories;
using Sidekick.Business.Languages;
using Sidekick.Core.Debounce;
using Sidekick.Core.Natives;
using Sidekick.Core.Settings;
using Sidekick.Extensions;
using Sidekick.Helpers;
using Sidekick.Localization.Prices;

namespace Sidekick.Views.Prices
{
    public class PriceViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly ILogger logger;
        private readonly IDebouncer debouncer;
        private readonly ITradeSearchService tradeSearchService;
        private readonly IPoeNinjaCache poeNinjaCache;
        private readonly IStaticDataService staticDataService;
        private readonly ILanguageProvider languageProvider;
        private readonly IPoePriceInfoClient poePriceInfoClient;
        private readonly INativeClipboard nativeClipboard;
        private readonly IParserService parserService;
        private readonly SidekickSettings settings;
        private readonly IStatDataService statDataService;
        private readonly IItemCategoryService itemCategoryService;

        public event PropertyChangedEventHandler PropertyChanged;

        public PriceViewModel(
            ILogger logger,
            IDebouncer debouncer,
            ITradeSearchService tradeSearchService,
            IPoeNinjaCache poeNinjaCache,
            IStaticDataService staticDataService,
            ILanguageProvider languageProvider,
            IPoePriceInfoClient poePriceInfoClient,
            INativeClipboard nativeClipboard,
            IParserService parserService,
            SidekickSettings settings,
            IStatDataService statDataService,
            IItemCategoryService itemCategoryService)
        {
            this.logger = logger;
            this.debouncer = debouncer;
            this.tradeSearchService = tradeSearchService;
            this.poeNinjaCache = poeNinjaCache;
            this.staticDataService = staticDataService;
            this.languageProvider = languageProvider;
            this.poePriceInfoClient = poePriceInfoClient;
            this.nativeClipboard = nativeClipboard;
            this.parserService = parserService;
            this.settings = settings;
            this.statDataService = statDataService;
            this.itemCategoryService = itemCategoryService;
            Task.Run(Initialize);

            PropertyChanged += PriceViewModel_PropertyChanged;
        }

        public Item Item { get; private set; }

        public string ItemColor => Item?.GetColor();

        public ObservableList<PriceItem> Results { get; private set; }

        public Uri Uri => QueryResult?.Uri;

        public bool IsError { get; private set; }
        public bool IsFetching { get; private set; }

        public bool ShowFilters => !IsError;
        public bool ShowList => !IsError && !ShowRefresh;
        public bool ShowRefresh { get; private set; } = true;
        public bool ShowPreview => !IsError;

        public ObservableList<PriceFilterCategory> Filters { get; set; }

        public ObservableDictionary<string, string> CategoryOptions { get; private set; } = new ObservableDictionary<string, string>();
        public string SelectedCategory { get; set; }
        public bool ShowCategory { get; set; }

        private async Task Initialize()
        {
            Item = await parserService.ParseItem(nativeClipboard.LastCopiedText);

            if (Item == null)
            {
                IsError = true;
                return;
            }

            CategoryOptions.Add(Item.TypeLine, null);
            // CategoryOptions.Add(PriceResources.Class_Any, null);
            if (Item.Category == Category.Weapon)
            {
                CategoryOptions.Add(PriceResources.Class_Weapon, "weapon");
                CategoryOptions.Add(PriceResources.Class_WeaponOne, "weapon.one");
                CategoryOptions.Add(PriceResources.Class_WeaponOneMelee, "weapon.onemelee");
                CategoryOptions.Add(PriceResources.Class_WeaponTwoMelee, "weapon.twomelee");
                CategoryOptions.Add(PriceResources.Class_WeaponBow, "weapon.bow");
                CategoryOptions.Add(PriceResources.Class_WeaponClaw, "weapon.claw");
                CategoryOptions.Add(PriceResources.Class_WeaponDagger, "weapon.dagger");
                CategoryOptions.Add(PriceResources.Class_WeaponRuneDagger, "weapon.runedagger");
                CategoryOptions.Add(PriceResources.Class_WeaponOneAxe, "weapon.oneaxe");
                CategoryOptions.Add(PriceResources.Class_WeaponOneMace, "weapon.onemace");
                CategoryOptions.Add(PriceResources.Class_WeaponOneSword, "weapon.onesword");
                CategoryOptions.Add(PriceResources.Class_WeaponSceptre, "weapon.sceptre");
                CategoryOptions.Add(PriceResources.Class_WeaponStaff, "weapon.staff");
                CategoryOptions.Add(PriceResources.Class_WeaponWarstaff, "weapon.warstaff");
                CategoryOptions.Add(PriceResources.Class_WeaponTwoAxe, "weapon.twoaxe");
                CategoryOptions.Add(PriceResources.Class_WeaponTwoMace, "weapon.twomace");
                CategoryOptions.Add(PriceResources.Class_WeaponTwoSword, "weapon.twosword");
                CategoryOptions.Add(PriceResources.Class_WeaponWand, "weapon.wand");
                CategoryOptions.Add(PriceResources.Class_WeaponRod, "weapon.rod");
            }

            if (Item.Category == Category.Armour)
            {
                CategoryOptions.Add(PriceResources.Class_Armour, "armour");
                CategoryOptions.Add(PriceResources.Class_ArmourChest, "armour.chest");
                CategoryOptions.Add(PriceResources.Class_ArmourBoots, "armour.boots");
                CategoryOptions.Add(PriceResources.Class_ArmourGloves, "armour.gloves");
                CategoryOptions.Add(PriceResources.Class_ArmourHelmet, "armour.helmet");
                CategoryOptions.Add(PriceResources.Class_ArmourShield, "armour.shield");
                CategoryOptions.Add(PriceResources.Class_ArmourQuiver, "armour.quiver");
            }

            if (Item.Category == Category.Accessory)
            {
                CategoryOptions.Add(PriceResources.Class_Accessory, "accessory");
                CategoryOptions.Add(PriceResources.Class_AccessoryAmulet, "accessory.amulet");
                CategoryOptions.Add(PriceResources.Class_AccessoryBelt, "accessory.belt");
                CategoryOptions.Add(PriceResources.Class_AccessoryRing, "accessory.ring");
            }

            if (Item.Category == Category.Gem)
            {
                CategoryOptions.Add(PriceResources.Class_Gem, "gem");
                CategoryOptions.Add(PriceResources.Class_GemActive, "gem.activegem");
                CategoryOptions.Add(PriceResources.Class_GemSupport, "gem.supportgem");
                CategoryOptions.Add(PriceResources.Class_GemAwakenedSupport, "gem.supportgemplus");
            }

            if (Item.Category == Category.Jewel)
            {
                CategoryOptions.Add(PriceResources.Class_Jewel, "jewel");
                CategoryOptions.Add(PriceResources.Class_JewelBase, "jewel.base");
                CategoryOptions.Add(PriceResources.Class_JewelAbyss, "jewel.abyss");
                CategoryOptions.Add(PriceResources.Class_JewelCluster, "jewel.cluster");
            }

            if (Item.Category == Category.Flask)
            {
                CategoryOptions.Add(PriceResources.Class_Flask, "flask");
            }

            if (Item.Category == Category.Map)
            {
                CategoryOptions.Add(PriceResources.Class_Map, "map");
                CategoryOptions.Add(PriceResources.Class_MapFragment, "map.fragment");
                CategoryOptions.Add(PriceResources.Class_MapScarab, "map.scarab");
            }

            if (Item.Category == Category.Watchstone)
            {
                CategoryOptions.Add(PriceResources.Class_Watchstone, "watchstone");
            }

            if (Item.Category == Category.Leaguestone)
            {
                CategoryOptions.Add(PriceResources.Class_Leaguestone, "leaguestone");
            }

            if (Item.Category == Category.Prophecy)
            {
                CategoryOptions.Add(PriceResources.Class_Prophecy, "prophecy");
            }

            if (Item.Category == Category.DivinationCard)
            {
                CategoryOptions.Add(PriceResources.Class_Card, "card");
            }

            if (Item.Category == Category.ItemisedMonster)
            {
                CategoryOptions.Add(PriceResources.Class_MonsterBeast, "monster.beast");
                CategoryOptions.Add(PriceResources.Class_MonsterSample, "monster.sample");
            }

            if (Item.Category == Category.Currency)
            {
                CategoryOptions.Add(PriceResources.Class_Currency, "currency");
                CategoryOptions.Add(PriceResources.Class_CurrencyPiece, "currency.piece");
                CategoryOptions.Add(PriceResources.Class_CurrencyResonator, "currency.resonator");
                CategoryOptions.Add(PriceResources.Class_CurrencyFossil, "currency.fossil");
                CategoryOptions.Add(PriceResources.Class_CurrencyIncubator, "currency.incubator");
            }

            SelectedCategory = (await itemCategoryService.Get(Item.TypeLine))?.Category;
            if (!CategoryOptions.Values.Any(x => x == SelectedCategory))
            {
                SelectedCategory = null;
            }

            ShowCategory = (Item?.Rarity == Rarity.Rare || Item?.Rarity == Rarity.Magic || Item?.Rarity == Rarity.Normal) && CategoryOptions.Count > 1;

            InitializeFilters();

            await UpdateQuery();

            GetPoeNinjaPrice();

            if (settings.EnablePricePrediction)
            {
                _ = GetPredictionPrice();
            }
        }

        private void InitializeFilters()
        {
            // No filters for prophecies, currencies and divination cards, etc.
            if (Item.Category == Category.DivinationCard
                || Item.Category == Category.Currency
                || Item.Category == Category.Prophecy
                || Item.Category == Category.ItemisedMonster
                || Item.Category == Category.Leaguestone
                || Item.Category == Category.Watchstone
                || Item.Category == Category.Undefined)
            {
                Filters = null;
                return;
            }

            Filters = new ObservableList<PriceFilterCategory>();

            var propertyCategory1 = new PriceFilterCategory();
            var propertyCategory2 = new PriceFilterCategory();

            // Quality
            InitializeFilter(propertyCategory1, nameof(SearchFilters.MiscFilters), nameof(MiscFilter.Quality), languageProvider.Language.DescriptionQuality, Item.Properties.Quality,
                enabled: Item.Rarity == Rarity.Gem,
                min: Item.Rarity == Rarity.Gem && Item.Properties.Quality >= 20 ? (double?)Item.Properties.Quality : null);

            // Armour
            InitializeFilter(propertyCategory1, nameof(SearchFilters.ArmourFilters), nameof(ArmorFilter.Armor), languageProvider.Language.DescriptionArmour, Item.Properties.Armor);
            // Evasion
            InitializeFilter(propertyCategory1, nameof(SearchFilters.ArmourFilters), nameof(ArmorFilter.Evasion), languageProvider.Language.DescriptionEvasion, Item.Properties.Evasion);
            // Energy shield
            InitializeFilter(propertyCategory1, nameof(SearchFilters.ArmourFilters), nameof(ArmorFilter.EnergyShield), languageProvider.Language.DescriptionEnergyShield, Item.Properties.EnergyShield);
            // Block
            InitializeFilter(propertyCategory1, nameof(SearchFilters.ArmourFilters), nameof(ArmorFilter.Block), languageProvider.Language.DescriptionChanceToBlock, Item.Properties.ChanceToBlock,
                delta: 1);

            // Gem level
            InitializeFilter(propertyCategory1, nameof(SearchFilters.MiscFilters), nameof(MiscFilter.GemLevel), languageProvider.Language.DescriptionLevel, Item.Properties.GemLevel,
                enabled: true,
                min: Item.Properties.GemLevel);

            // Item quantity
            InitializeFilter(propertyCategory1, nameof(SearchFilters.MapFilters), nameof(MapFilter.ItemQuantity), languageProvider.Language.DescriptionItemQuantity, Item.Properties.ItemQuantity);
            // Item rarity
            InitializeFilter(propertyCategory1, nameof(SearchFilters.MapFilters), nameof(MapFilter.ItemRarity), languageProvider.Language.DescriptionItemRarity, Item.Properties.ItemRarity);
            // Monster pack size
            InitializeFilter(propertyCategory1, nameof(SearchFilters.MapFilters), nameof(MapFilter.MonsterPackSize), languageProvider.Language.DescriptionMonsterPackSize, Item.Properties.MonsterPackSize);
            // Blighted
            InitializeFilter(propertyCategory1, nameof(SearchFilters.MapFilters), nameof(MapFilter.Blighted), languageProvider.Language.PrefixBlighted, Item.Properties.Blighted,
                enabled: Item.Properties.Blighted);
            // Map tier
            InitializeFilter(propertyCategory1, nameof(SearchFilters.MapFilters), nameof(MapFilter.MapTier), languageProvider.Language.DescriptionMapTier, Item.Properties.MapTier,
                enabled: true,
                min: Item.Properties.MapTier);

            // Physical Dps
            InitializeFilter(propertyCategory1, nameof(SearchFilters.WeaponFilters), nameof(WeaponFilter.PhysicalDps), PriceResources.Filters_PDps, Item.Properties.PhysicalDps);
            // Elemental Dps
            InitializeFilter(propertyCategory1, nameof(SearchFilters.WeaponFilters), nameof(WeaponFilter.ElementalDps), PriceResources.Filters_EDps, Item.Properties.ElementalDps);
            // Total Dps
            InitializeFilter(propertyCategory1, nameof(SearchFilters.WeaponFilters), nameof(WeaponFilter.DamagePerSecond), PriceResources.Filters_Dps, Item.Properties.DamagePerSecond);
            // Attacks per second
            InitializeFilter(propertyCategory1, nameof(SearchFilters.WeaponFilters), nameof(WeaponFilter.AttacksPerSecond), languageProvider.Language.DescriptionAttacksPerSecond, Item.Properties.AttacksPerSecond,
                delta: 0.1);
            // Critical strike chance
            InitializeFilter(propertyCategory1, nameof(SearchFilters.WeaponFilters), nameof(WeaponFilter.CriticalStrikeChance), languageProvider.Language.DescriptionCriticalStrikeChance, Item.Properties.CriticalStrikeChance,
                delta: 1);

            // Item level
            InitializeFilter(propertyCategory2, nameof(SearchFilters.MiscFilters), nameof(MiscFilter.ItemLevel), languageProvider.Language.DescriptionItemLevel, Item.ItemLevel,
                enabled: Item.ItemLevel >= 80 && Item.Properties.MapTier == 0 && Item.Rarity != Rarity.Unique,
                min: Item.ItemLevel >= 80 ? (double?)Item.ItemLevel : null);

            // Corrupted
            InitializeFilter(propertyCategory2, nameof(SearchFilters.MiscFilters), nameof(MiscFilter.Corrupted), languageProvider.Language.DescriptionCorrupted, Item.Corrupted,
                alwaysIncluded: Item.Rarity == Rarity.Gem || Item.Rarity == Rarity.Unique,
                enabled: (Item.Rarity == Rarity.Gem || Item.Rarity == Rarity.Unique) && Item.Corrupted,
                applyNegative: true);

            // Crusader
            InitializeFilter(propertyCategory2, nameof(SearchFilters.MiscFilters), nameof(MiscFilter.CrusaderItem), languageProvider.Language.InfluenceCrusader, Item.Influences.Crusader,
                enabled: Item.Influences.Crusader);
            // Elder
            InitializeFilter(propertyCategory2, nameof(SearchFilters.MiscFilters), nameof(MiscFilter.ElderItem), languageProvider.Language.InfluenceElder, Item.Influences.Elder,
                enabled: Item.Influences.Elder);
            // Hunter
            InitializeFilter(propertyCategory2, nameof(SearchFilters.MiscFilters), nameof(MiscFilter.HunterItem), languageProvider.Language.InfluenceHunter, Item.Influences.Hunter,
                enabled: Item.Influences.Hunter);
            // Redeemer
            InitializeFilter(propertyCategory2, nameof(SearchFilters.MiscFilters), nameof(MiscFilter.RedeemerItem), languageProvider.Language.InfluenceRedeemer, Item.Influences.Redeemer,
                enabled: Item.Influences.Redeemer);
            // Shaper
            InitializeFilter(propertyCategory2, nameof(SearchFilters.MiscFilters), nameof(MiscFilter.ShaperItem), languageProvider.Language.InfluenceShaper, Item.Influences.Shaper,
                enabled: Item.Influences.Shaper);
            // Warlord
            InitializeFilter(propertyCategory2, nameof(SearchFilters.MiscFilters), nameof(MiscFilter.WarlordItem), languageProvider.Language.InfluenceWarlord, Item.Influences.Warlord,
                enabled: Item.Influences.Warlord);

            if (propertyCategory1.Filters.Any())
            {
                Filters.Add(propertyCategory1);
            }

            if (propertyCategory2.Filters.Any())
            {
                Filters.Add(propertyCategory2);
            }

            // Modifiers
            InitializeMods(Item.Modifiers.Pseudo);
            InitializeMods(Item.Modifiers.Enchant, false);
            InitializeMods(Item.Modifiers.Implicit);
            InitializeMods(Item.Modifiers.Explicit);
            InitializeMods(Item.Modifiers.Crafted);
            InitializeMods(Item.Modifiers.Fractured);

            if (Filters.Count == 0)
            {
                Filters = null;
            }
        }

        private void InitializeMods(List<Modifier> modifiers, bool normalizeValues = true)
        {
            if (modifiers.Count == 0)
            {
                return;
            }

            PriceFilterCategory category = null;

            var settingMods = Item.Category switch
            {
                Category.Accessory => settings.AccessoryModifiers,
                Category.Armour => settings.ArmourModifiers,
                Category.Flask => settings.FlaskModifiers,
                Category.Jewel => settings.JewelModifiers,
                Category.Map => settings.MapModifiers,
                Category.Weapon => settings.WeaponModifiers,
                _ => new List<string>(),
            };

            foreach (var modifier in modifiers)
            {
                if (category == null)
                {
                    category = new PriceFilterCategory();
                }

                if (modifier.OptionValue != null)
                {
                    InitializeFilter(category,
                                     nameof(StatFilter),
                                     modifier.Id,
                                     !string.IsNullOrEmpty(modifier.Category) ? $"({modifier.Category}) {modifier.Text}" : modifier.Text,
                                     modifier.OptionValue,
                                     normalizeValues: normalizeValues,
                                     enabled: settingMods.Contains(modifier.Id) && Item.Rarity != Rarity.Unique
                    );
                }
                else
                {
                    InitializeFilter(category,
                                     nameof(StatFilter),
                                     modifier.Id,
                                     !string.IsNullOrEmpty(modifier.Category) ? $"({modifier.Category}) {modifier.Text}" : modifier.Text,
                                     modifier.Values,
                                     normalizeValues: normalizeValues,
                                     enabled: settingMods.Contains(modifier.Id) && Item.Rarity != Rarity.Unique
                    );
                }
            }

            if (category != null)
            {
                Filters.Add(category);
            }
        }

        private static readonly Regex LabelValues = new Regex("(\\#)");
        private void InitializeFilter<T>(PriceFilterCategory category,
                                         string type,
                                         string id,
                                         string label,
                                         T value,
                                         double delta = 5,
                                         bool enabled = false,
                                         double? min = null,
                                         double? max = null,
                                         bool alwaysIncluded = false,
                                         bool normalizeValues = true,
                                         bool applyNegative = false)
        {
            ModifierOption option = null;

            if (value is bool boolValue)
            {
                if (!boolValue && !alwaysIncluded)
                {
                    return;
                }
            }
            else if (value is int intValue)
            {
                if (intValue == 0 && !alwaysIncluded)
                {
                    return;
                }
                if (min == null && normalizeValues)
                {
                    min = NormalizeMinValue(intValue, delta);
                }
                if (LabelValues.IsMatch(label))
                {
                    label = LabelValues.Replace(label, intValue.ToString());
                }
                else
                {
                    label += $": {value}";
                }
            }
            else if (value is double doubleValue)
            {
                if (doubleValue == 0 && !alwaysIncluded)
                {
                    return;
                }
                if (min == null && normalizeValues)
                {
                    min = NormalizeMinValue(doubleValue, delta);
                }
                if (LabelValues.IsMatch(label))
                {
                    label = LabelValues.Replace(label, doubleValue.ToString("0.00"));
                }
                else
                {
                    label += $": {doubleValue:0.00}";
                }
            }
            else if (value is List<double> groupValue)
            {
                var itemValue = groupValue.OrderBy(x => x).FirstOrDefault();

                if (itemValue >= 0)
                {
                    min = itemValue;
                    if (normalizeValues)
                    {
                        min = NormalizeMinValue(min, delta);
                    }
                }
                else
                {
                    max = itemValue;
                    if (normalizeValues)
                    {
                        max = NormalizeMaxValue(max, delta);
                    }
                }

                if (!groupValue.Any())
                {
                    min = null;
                    max = null;
                }
            }
            else if (value is ModifierOption modifierOption)
            {
                option = modifierOption;
                min = null;
                max = null;
            }

            var priceFilter = new PriceFilter()
            {
                Enabled = enabled,
                Type = type,
                Id = id,
                Text = label,
                Min = min,
                Max = max,
                HasRange = min.HasValue || max.HasValue,
                ApplyNegative = applyNegative,
                Option = option,
            };

            priceFilter.PropertyChanged += (object sender, PropertyChangedEventArgs e) => { UpdateDebounce(); };

            category.Filters.Add(priceFilter);
        }

        private FetchResult<string> QueryResult { get; set; }

        /// <summary>
        /// Smallest positive value between a -5 delta or 90%.
        /// </summary>
        private int? NormalizeMinValue(double? value, double delta)
        {
            if (value.HasValue)
            {
                if (value.Value > 0)
                {
                    return (int)Math.Max(Math.Min(value.Value - delta, value.Value * 0.9), 0);
                }
                else
                {
                    return (int)Math.Min(Math.Min(value.Value - delta, value.Value * 1.1), 0);
                }
            }

            return null;
        }

        /// <summary>
        /// Smallest positive value between a -5 delta or 90%.
        /// </summary>
        private int? NormalizeMaxValue(double? value, double delta)
        {
            if (value.HasValue)
            {
                if (value.Value > 0)
                {
                    return (int)Math.Max(Math.Max(value.Value + delta, value.Value * 1.1), 0);
                }
                else
                {
                    return (int)Math.Min(Math.Max(value.Value + delta, value.Value * 0.9), 0);
                }
            }

            return null;
        }

        public int UpdateCountdown { get; private set; }
        public void UpdateDebounce()
        {
            ShowRefresh = true;

            _ = debouncer.Debounce("priceview", async () => await UpdateQuery(),
                delayUpdate: (countdown) =>
                {
                    UpdateCountdown = (int)Math.Round((decimal)countdown / 1000);
                });
        }

        public async Task UpdateQuery()
        {
            ShowRefresh = false;
            Results = new ObservableList<PriceItem>();

            if (Filters != null)
            {
                var saveSettings = false;

                var settingMods = Item.Category switch
                {
                    Category.Accessory => settings.AccessoryModifiers,
                    Category.Armour => settings.ArmourModifiers,
                    Category.Flask => settings.FlaskModifiers,
                    Category.Jewel => settings.JewelModifiers,
                    Category.Map => settings.MapModifiers,
                    Category.Weapon => settings.WeaponModifiers,
                    _ => new List<string>(),
                };

                foreach (var filter in Filters.SelectMany(x => x.Filters).Where(x => x.Type == nameof(StatFilter)))
                {
                    if (settingMods.Contains(filter.Id))
                    {
                        if (!filter.Enabled)
                        {
                            saveSettings = true;
                            settingMods.Remove(filter.Id);
                        }
                    }
                    else
                    {
                        if (filter.Enabled)
                        {
                            saveSettings = true;
                            settingMods.Add(filter.Id);
                        }
                    }
                }
                if (saveSettings)
                {
                    switch (Item.Category)
                    {
                        case Category.Accessory: settings.AccessoryModifiers = settingMods; break;
                        case Category.Armour: settings.ArmourModifiers = settingMods; break;
                        case Category.Flask: settings.FlaskModifiers = settingMods; break;
                        case Category.Jewel: settings.JewelModifiers = settingMods; break;
                        case Category.Map: settings.MapModifiers = settingMods; break;
                        case Category.Weapon: settings.WeaponModifiers = settingMods; break;
                    };

                    settings.Save();
                }
            }

            IsFetching = true;
            if (Item.Rarity == Rarity.Currency && staticDataService.GetId(Item) != null)
            {
                QueryResult = await tradeSearchService.SearchBulk(Item);
            }
            else
            {
                QueryResult = await tradeSearchService.Search(Item, GetFilters(), GetStats());
            }
            IsFetching = false;
            if (QueryResult == null)
            {
                IsError = true;
            }
            else if (QueryResult.Result.Any())
            {
                await LoadMoreData();
                await LoadMoreData();
            }

            UpdateCountString();
        }

        private List<StatFilter> GetStats()
        {
            return Filters?
                .SelectMany(x => x.Filters)
                .Where(x => x.Type == nameof(StatFilter))
                .Where(x => x.Enabled)
                .Select(x => new StatFilter()
                {
                    Disabled = !x.Enabled,
                    Id = x.Id,
                    Value = new SearchFilterValue()
                    {
                        Max = x.Max,
                        Min = x.Min,
                        Option = x.Option?.Value,
                    }
                })
                .ToList();
        }

        private SearchFilters GetFilters()
        {
            var filters = Filters?
                .SelectMany(x => x.Filters);

            if (filters == null)
            {
                return null;
            }

            var searchFilters = new SearchFilters();

            foreach (var filter in filters)
            {
                var property = searchFilters.GetType().GetProperty(filter.Type);
                if (property == null)
                {
                    continue;
                }

                var typeObject = property.GetValue(searchFilters);
                property = typeObject.GetType().GetProperty("Filters");
                if (property == null)
                {
                    continue;
                }

                var filtersObject = property.GetValue(typeObject);
                property = filtersObject.GetType().GetProperty(filter.Id);
                if (property == null)
                {
                    continue;
                }

                if (property.PropertyType == typeof(SearchFilterOption) && (filter.Enabled || filter.ApplyNegative))
                {
                    property.SetValue(filtersObject, new SearchFilterOption(filter.Enabled ? "true" : "false"));
                }
                else if (property.PropertyType == typeof(SearchFilterValue))
                {
                    if (!filter.Enabled)
                    {
                        continue;
                    }
                    var valueObject = new SearchFilterValue
                    {
                        Max = filter.Max,
                        Min = filter.Min,
                        Option = filter.Option?.Value,
                    };
                    property.SetValue(filtersObject, valueObject);
                }
            }

            searchFilters.TypeFilters.Filters.Category = new SearchFilterOption(SelectedCategory);

            return searchFilters;
        }

        public async Task LoadMoreData()
        {
            if (IsFetching)
            {
                return;
            }

            try
            {
                var ids = QueryResult.Result.Skip(Results?.Count ?? 0).Take(10).ToList();
                if (ids.Count == 0)
                {
                    return;
                }

                IsFetching = true;
                var getResult = await tradeSearchService.GetResults(QueryResult.Id, ids, GetStats());
                IsFetching = false;

                if (getResult != null && getResult.Result.Any())
                {
                    Results.AddRange(getResult.Result.Select(result => new PriceItem(result)
                    {
                        ImageUrl = new Uri(
                            languageProvider.Language.PoeCdnBaseUrl,
                            staticDataService.GetImage(result.Listing.Price.Currency)
                        ).AbsoluteUri,
                    }));
                }

                UpdateCountString();
            }
            catch (Exception e)
            {
                logger.Error(e, "Error loading more data");
            }
        }

        public bool IsPredicted => !string.IsNullOrEmpty(PredictionText);
        public string PredictionText { get; private set; }
        private async Task GetPredictionPrice()
        {
            PredictionText = string.Empty;

            if (!IsPoeNinja)
            {
                var result = await poePriceInfoClient.GetItemPricePrediction(Item.Text);
                if (result?.ErrorCode != 0)
                {
                    return;
                }

                PredictionText = string.Format(
                    PriceResources.PredictionString,
                    $"{result.Min?.ToString("N1")}-{result.Max?.ToString("N1")} {result.Currency}",
                    result.ConfidenceScore.ToString("N1"));
            }
        }

        public bool IsPoeNinja => !string.IsNullOrEmpty(PoeNinjaText);
        public string PoeNinjaText { get; private set; }
        private void GetPoeNinjaPrice()
        {
            PoeNinjaText = string.Empty;

            var poeNinjaItemPriceInChaos = poeNinjaCache.GetItemPrice(Item);
            if (poeNinjaItemPriceInChaos != null)
            {
                PoeNinjaText = string.Format(PriceResources.PoeNinjaString,
                                             poeNinjaItemPriceInChaos.Value.ToString("N3"),
                                             poeNinjaCache.LastRefreshTimestamp.Value.ToString("t"));
            }
        }

        public bool FullyLoaded { get; private set; }
        public string CountString { get; private set; }
        private void UpdateCountString()
        {
            FullyLoaded = (Results?.Count ?? 0) == (QueryResult?.Result?.Count ?? 0);
            CountString = string.Format(PriceResources.CountString, Results?.Count ?? 0, QueryResult?.Total.ToString() ?? "?");
        }

        public bool HasPreviewItem { get; private set; }
        public PriceItem PreviewItem { get; private set; }
        public void Preview(PriceItem selectedItem)
        {
            PreviewItem = selectedItem;
            HasPreviewItem = PreviewItem != null;
        }

        private void PriceViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SelectedCategory))
            {
                if (string.IsNullOrEmpty(SelectedCategory))
                {
                    _ = itemCategoryService.Delete(Item.TypeLine);
                }
                else
                {
                    _ = itemCategoryService.SaveCategory(Item.TypeLine, SelectedCategory);
                }
                UpdateDebounce();
            }
        }

        public void Dispose()
        {
            PropertyChanged -= PriceViewModel_PropertyChanged;
        }
    }
}
