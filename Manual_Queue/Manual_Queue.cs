//ArchiSteamFarm-5.1.0.9
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.Plugins.Interfaces;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Steam.Integration;
using ArchiSteamFarm.Steam.Interaction;
using ArchiSteamFarm.Steam.Storage;
using ArchiSteamFarm.Web.Responses;
using JetBrains.Annotations;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace Manual_Queue
{
	[Export(typeof(IPlugin))]
	public sealed class Manual_Queue : IBotCommand
	{
		public string Name => "Manual_Queue By Cappi_1998";
		public Version Version => typeof(Manual_Queue).Assembly.GetName().Version ?? throw new InvalidOperationException(nameof(Version));

		public void OnLoaded(){}

		public async Task<string> OnBotCommand([NotNull] Bot bot, ulong steamID, [NotNull] string message, [ItemNotNull, NotNull] string[] args)
		{
			if (!bot.HasAccess(steamID, BotConfig.EAccess.Master))
			{
				return null;
			}

			switch (args[0].ToUpperInvariant())
			{
				case "QUEUE" when args.Length > 1:
					return await ResponseQueue(Utilities.GetArgsAsText(args, 1, ",")).ConfigureAwait(false);
				case "QUEUE":
					return await ResponseQueue(bot.BotName).ConfigureAwait(false);
				default:
					return null;
			}
		}

		private static async Task<string> ResponseQueue(string botNames)
		{
			HashSet<Bot> bots = Bot.GetBots(botNames);
			if ((bots == null) || (bots.Count == 0))
			{
				return Commands.FormatStaticResponse(string.Format(Strings.BotNotFound, botNames));
			}

			IList<string> results = await Utilities.InParallel(bots.Select(bot => ExploreQueue(bot))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result)));

			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}

		private static async Task<string> ExploreQueue(Bot bot)
		{
			if (!bot.IsConnectedAndLoggedOn) return null;

			bot.ArchiLogger.LogGenericTrace(Strings.Starting);

			ImmutableHashSet<uint> queue = await GenerateNewDiscoveryQueue(bot).ConfigureAwait(false);

			if ((queue == null) || (queue.Count == 0))
			{
				bot.ArchiLogger.LogGenericTrace(string.Format(Strings.ErrorIsEmpty, nameof(queue)));

				return null;
			}

			// We could in theory do this in parallel, but who knows what would happen...
			foreach (uint queuedAppID in queue)
			{
				if (await ClearFromDiscoveryQueue(bot, queuedAppID).ConfigureAwait(false)) continue;

				bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);

				return null;
			}

			bot.ArchiLogger.LogGenericTrace(Strings.Done);
			return $"<{bot.BotName}> Done!";
		}

		static async Task<ImmutableHashSet<uint>?> GenerateNewDiscoveryQueue(Bot bot)
		{
			ASF.ArchiLogger.LogGenericInfo($"<{bot.BotName}> - {Strings.Executing}");

			Uri request = new(ArchiWebHandler.SteamStoreURL, "/explore/generatenewdiscoveryqueue");

			// Extra entry for sessionID
			Dictionary<string, string> data = new(2, StringComparer.Ordinal) { { "queuetype", "0" } };

			ObjectResponse<NewDiscoveryQueueResponse>? response = await bot.ArchiWebHandler.UrlPostToJsonObjectWithSession<NewDiscoveryQueueResponse>(request, data: data).ConfigureAwait(false);

			return response?.Content.Queue;
		}

		static async Task<bool> ClearFromDiscoveryQueue(Bot bot, uint appID)
		{
			ASF.ArchiLogger.LogGenericInfo($"<{bot.BotName}> - AppID:{appID}.");

			if (appID == 0)
			{
				throw new ArgumentOutOfRangeException(nameof(appID));
			}

			Uri request = new(ArchiWebHandler.SteamStoreURL, "/app/" + appID);

			// Extra entry for sessionID
			Dictionary<string, string> data = new(2, StringComparer.Ordinal) { { "appid_to_clear_from_queue", appID.ToString(CultureInfo.InvariantCulture) } };

			return await bot.ArchiWebHandler.UrlPostWithSession(request, data: data).ConfigureAwait(false);
		}

	}

	internal sealed class NewDiscoveryQueueResponse
	{
		[JsonProperty(PropertyName = "queue", Required = Required.Always)]
		internal readonly ImmutableHashSet<uint> Queue;

		[JsonConstructor]
		private NewDiscoveryQueueResponse() { }
	}
}
