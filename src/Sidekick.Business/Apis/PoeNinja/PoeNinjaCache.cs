using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Serilog;
using Sidekick.Business.Apis.Poe.Models;
using Sidekick.Business.Apis.Poe.Trade.Leagues;
using Sidekick.Business.Apis.PoeNinja.Models;
using Sidekick.Business.Languages;
using Sidekick.Core.Initialization;
using Sidekick.Core.Settings;

namespace Sidekick.Business.Apis.PoeNinja
{

    /// <summary>
    /// poe.ninja cache.
    /// Fetch poe.ninja with specified interval in the background.
    /// Alternatively give the user the option to refresh the cache via TrayIcon or Shortcut.
    /// </summary>
    public class PoeNinjaCache : IPoeNinjaCache, IOnAfterInit
    {
        private readonly IPoeNinjaClient client;
        private readonly ILogger logger;
        private readonly ILanguageProvider languageProvider;
        private readonly SidekickSettings configuration;

        public DateTime? LastRefreshTimestamp { get; private set; }

        public List<PoeNinjaItem> Items { get; private set; } = new List<PoeNinjaItem>();
        public List<PoeNinjaCurrency> Currencies { get; private set; } = new List<PoeNinjaCurrency>();
        public Dictionary<string, string> Translations { get; private set; } = new Dictionary<string, string>();

        public bool IsInitialized => LastRefreshTimestamp.HasValue;

        public PoeNinjaCache(IPoeNinjaClient client,
                             ILogger logger,
                             ILanguageProvider languageProvider,
                             ILeagueDataService leagueDataService,
                             SidekickSettings configuration)
        {
            this.client = client;
            this.languageProvider = languageProvider;
            this.logger = logger.ForContext(GetType());
            this.configuration = configuration;

            leagueDataService.OnLeagueChange += async () => await RefreshData();
        }
        public PoeNinjaItem GetItem(Item item)
        {
            string nameToSearch = item.Type.Contains(languageProvider.Language.KeywordVaal) ? item.Type : item.NameLine;
            string translatedName = null; // PoeNinja doesn't translate all items, example : Tabula Rasa.

            if (client.IsSupportingCurrentLanguage && Translations.Any())
            {
                Translations.TryGetValue(nameToSearch, out translatedName);
            }

            var query = Items.Where(x => (x.Name == nameToSearch || x.Name == translatedName) && x.Corrupted == item.Corrupted);

            if (item.Properties.MapTier > 0) query = query.Where(x => x.MapTier == item.Properties.MapTier);

            if (item.Properties.GemLevel > 0) query = query.Where(x => x.GemLevel == item.Properties.GemLevel && x.GemQuality == item.Properties.Quality);

            // For some reason there seems to be duplicates with higher values (legacy items?), we always take the lowest value item.
            return query.OrderBy(x => x.ChaosValue).FirstOrDefault();
        }

        public PoeNinjaCurrency GetCurrency(Item item)
        {
            return Currencies.FirstOrDefault(x => x.CurrencyTypeName == item.NameLine);
        }

        public double? GetItemPrice(Item item)
        {
            return GetCurrency(item)?.Receive.Value ?? GetItem(item)?.ChaosValue;
        }

        public async Task RefreshData()
        {
            Items = new List<PoeNinjaItem>();
            Currencies = new List<PoeNinjaCurrency>();
            Translations = new Dictionary<string, string>();

            if (!client.IsSupportingCurrentLanguage)
            {
                logger.Information($"PoeNinja doesn't support this language.");
                return;
            }

            logger.Information($"Populating PoeNinja cache.");

            var itemsTasks = Enum.GetValues(typeof(ItemType))
                                 .Cast<ItemType>()
                                 .Select(x => new { itemType = x, request = client.QueryItem(configuration.LeagueId, x) })
                                 .ToList();
            var currenciesTasks = Enum.GetValues(typeof(CurrencyType))
                                      .Cast<CurrencyType>()
                                      .Select(x => new { currencyType = x, request = client.QueryItem(configuration.LeagueId, x) })
                                      .ToList();

            await Task.WhenAll(itemsTasks.Select(x => x.request).Cast<Task>().Concat(currenciesTasks.Select(x => x.request).Cast<Task>()));

            Items = itemsTasks.Select(x => new PoeNinjaCacheItem<PoeNinjaItem> { Type = x.itemType.ToString(), Items = x.request.Result.Lines }).SelectMany(x => x.Items).ToList();
            Currencies = currenciesTasks.Select(x => new PoeNinjaCacheItem<PoeNinjaCurrency> { Type = x.currencyType.ToString(), Items = x.request.Result.Lines }).SelectMany(x => x.Items).ToList();

            // PoeNinja also includes translations of an item's description,
            // we will strip those by supposing that they always end with a dot (end of sentence).
            var flattenedTranslations = itemsTasks.Select(x => x.request.Result.Language?.Translations?.Where(y => !y.Value.Contains(".")))
                                                  .Where(x => x != null)
                                                  .SelectMany(x => x)
                                                  .Distinct()
                                                  .ToDictionary(x => x.Key, x => x.Value);

            if (flattenedTranslations.Any())
            {
                // We flip the dictionary to use the value instead of the key and ignore duplicates.
                Translations = flattenedTranslations.GroupBy(x => x.Value).Select(x => x.First()).ToDictionary(x => x.Value, x => x.Key);
            }

            LastRefreshTimestamp = DateTime.Now;

            logger.Information($"PoeNinja cache populated.");

            return;
        }

        public Task OnAfterInit() => RefreshData();
    }
}
