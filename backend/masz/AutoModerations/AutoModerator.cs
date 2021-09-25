using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using masz.Enums;
using masz.Exceptions;
using masz.Models;
using masz.Repositories;
using masz.Services;
using Microsoft.Extensions.Logging;

namespace masz.AutoModerations
{
    public class AutoModerator
    {
        private readonly ILogger<AutoModerator> _logger;
        private readonly DiscordClient _client;
        private readonly IServiceProvider _serviceProvider;
        private readonly IInternalConfiguration _config;
        private readonly IDiscordAnnouncer _announcer;
        private readonly IDatabase _database;
        private readonly GuildConfig _guildConfig;
        private readonly List<AutoModerationConfig> _autoModerationConfigs;

        private AutoModerator(DiscordClient client, IServiceProvider serviceProvider, GuildConfig guildConfig, List<AutoModerationConfig> autoModerationConfigs)
        {
            _client = client;
            _serviceProvider = serviceProvider;
            _logger = (ILogger<AutoModerator>) _serviceProvider.GetService(typeof(ILogger<AutoModerator>));
            _database = (IDatabase) _serviceProvider.GetService(typeof(IDatabase));
            _config = (IInternalConfiguration) _serviceProvider.GetService(typeof(IInternalConfiguration));
            _announcer = (IDiscordAnnouncer) _serviceProvider.GetService(typeof(IDiscordAnnouncer));

            _guildConfig = guildConfig;
            _autoModerationConfigs = autoModerationConfigs;
        }

        public static async Task<AutoModerator> CreateDefault(DiscordClient client, ulong guildId, IServiceProvider serviceProvider)
        {
            var guildConfig = await GuildConfigRepository.CreateDefault(serviceProvider).GetGuildConfig(guildId);
            var autoModerationConfigs = await AutoModerationConfigRepository.CreateDefault(serviceProvider).GetConfigsByGuild(guildId);
            return new AutoModerator(client, serviceProvider, guildConfig, autoModerationConfigs);
        }

        public async Task HandleAutomoderation(DiscordMessage message, bool onEdit = false)
        {
            if (message.MessageType != MessageType.Default && message.MessageType != MessageType.Reply)
            {
                return;
            }
            if (message.Author.IsBot)
            {
                return;
            }
            if (message.Channel.Guild == null)
            {
                return;
            }

            // invites
            if (await CheckAutoMod(
                    AutoModerationType.InvitePosted,
                    message,
                    InviteChecker.Check
                )) return;

            // emotes
            if (await CheckAutoMod(
                    AutoModerationType.TooManyEmotes,
                    message,
                    EmoteCheck.Check
                )) return;

            // mentions
            if (await CheckAutoMod(
                    AutoModerationType.TooManyMentions,
                    message,
                    MentionCheck.Check
                )) return;

            // attachments
            if (await CheckAutoMod(
                    AutoModerationType.TooManyAttachments,
                    message,
                    AttachmentCheck.Check
                )) return;

            // attachments
            if (await CheckAutoMod(
                    AutoModerationType.TooManyEmbeds,
                    message,
                    EmbedCheck.Check
                )) return;

            // too many automods

            // custom
            if (await CheckAutoMod(
                    AutoModerationType.CustomWordFilter,
                    message,
                    CustomWordCheck.Check
                )) return;

            // spam check
            if (! onEdit)
            {
                if (await CheckAutoMod(
                       AutoModerationType.TooManyMessages,
                        message,
                        SpamCheck.Check
                    )) return;
            }
        }

        private async Task<bool> CheckAutoMod(AutoModerationType autoModerationType, DiscordMessage message, Func<DiscordMessage, AutoModerationConfig, bool> predicate)
        {
            AutoModerationConfig autoModerationConfig = _autoModerationConfigs.FirstOrDefault(x => x.AutoModerationType == autoModerationType);
            if (autoModerationConfig != null)
            {
                if (predicate(message, autoModerationConfig))
                {
                    if (! await IsProtectedByFilter(message, autoModerationConfig))
                    {
                        _logger.LogInformation($"U: {message.Author.Id} | C: {message.Channel.Id} | G: {message.Channel.Guild.Id} triggered {autoModerationConfig.AutoModerationType.ToString()}.");
                        await ExecutePunishment(message, autoModerationConfig, _guildConfig);
                        await CheckMultipleEvents(message,  _autoModerationConfigs.FirstOrDefault(x => x.AutoModerationType == AutoModerationType.TooManyAutoModerations));
                        return true;
                    }
                }
            }
            return false;
        }

        private async Task<bool> IsProtectedByFilter(DiscordMessage message, AutoModerationConfig autoModerationConfig)
        {
            if (_config.GetSiteAdmins().Contains(message.Author.Id))
            {
                return true;
            }

            DiscordGuild guild = await _client.GetGuildAsync(message.Channel.Guild.Id);
            DiscordMember member = await guild.GetMemberAsync(message.Author.Id);

            if (member == null)
            {
                return false;
            }

            if (member.Roles.Any(x => _guildConfig.ModRoles.Contains(x.Id) ||
                                      _guildConfig.AdminRoles.Contains(x.Id) ||
                                      autoModerationConfig.IgnoreRoles.Contains(x.Id)))
            {
                return true;
            }

            return autoModerationConfig.IgnoreChannels.Contains(message.Channel.Id);
        }

        private async Task CheckMultipleEvents(DiscordMessage message, AutoModerationConfig? autoModerationConfig)
        {
        }

        private async Task ExecutePunishment(DiscordMessage message, AutoModerationConfig autoModerationConfig, GuildConfig guildConfig)
        {
            // internal notification
            // dm notification
            // register event
            // delete content if needed
            // notification in current channel if content deleted
        }

    }
}