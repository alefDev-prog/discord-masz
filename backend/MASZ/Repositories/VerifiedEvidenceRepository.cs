﻿using Discord;
using MASZ.Exceptions;
using MASZ.Models;
using MASZ.Models.Database;

namespace MASZ.Repositories
{
    public class VerifiedEvidenceRepository : BaseRepository<VerifiedEvidenceRepository>
    {
        IUser _currentUser;

        private VerifiedEvidenceRepository(IServiceProvider serviceProvider, IUser currentUser) : base(serviceProvider)
        {
            _currentUser = currentUser;
        }

        private VerifiedEvidenceRepository(IServiceProvider serviceProvider) : base(serviceProvider)
        {
            _currentUser = DiscordAPI.GetCurrentBotInfo();
        }

        public static VerifiedEvidenceRepository CreateDefault(IServiceProvider serviceProvider, Identity identity) => new(serviceProvider, identity.GetCurrentUser());
        public static VerifiedEvidenceRepository CreateDefault(IServiceProvider serviceProvider, IUser user) => new(serviceProvider, user);
        public static VerifiedEvidenceRepository CreateWithBotIdentity(IServiceProvider serviceProvider) => new(serviceProvider);

        public async Task<List<VerifiedEvidence>> GetAllEvidence(ulong guildId)
        {
            return await Database.GetAllEvidence(guildId);
        }

        public async Task<VerifiedEvidence> GetEvidence(ulong guildId, int evidenceId)
        {
            return await Database.GetEvidence(guildId, evidenceId);
        }

        public async Task<VerifiedEvidence> DeleteEvidence(ulong guildId, int evidenceId)
        {
            VerifiedEvidence evidence = await GetEvidence(guildId, evidenceId);
            if (evidence != default)
            {
                Database.DeleteEvidence(evidence);
                await Database.SaveChangesAsync();
            }
            return evidence;
        }

        public async Task<VerifiedEvidence> CreateEvidence(VerifiedEvidence evidence)
        {
            Database.CreateEvidence(evidence);
            await Database.SaveChangesAsync();
            return evidence;
        }

        public async Task Link(ulong guildId, int evidenceId, int caseId)
        {
            ModCaseEvidenceMapping existing = await Database.GetModCaseEvidenceMapping(evidenceId, caseId);
            if (existing != null)
            {
                throw new BaseAPIException("Cases are already linked.");
            }

            VerifiedEvidence evidence = await GetEvidence(guildId, evidenceId);
            ModCase modCase = await ModCaseRepository.CreateWithBotIdentity(_serviceProvider).GetModCase(guildId, caseId);

            ModCaseEvidenceMapping newMapping = new() 
            {
                Evidence = evidence,
                ModCase = modCase
            };

            Database.CreateModCaseEvidenceMapping(newMapping);
            await Database.SaveChangesAsync();
        }

        public async Task Unlink(ulong guildId, int evidenceId, int caseId)
        {


            VerifiedEvidence evidence = await GetEvidence(guildId, evidenceId);
            ModCase modCase = await ModCaseRepository.CreateWithBotIdentity(_serviceProvider).GetModCase(guildId, caseId);

            ModCaseEvidenceMapping mapping = await Database.GetModCaseEvidenceMapping(evidence.Id, modCase.Id);

            if (mapping == null)
            {
                throw new BaseAPIException("Cases are not linked.");
            }

            Database.DeleteModCaseEvidenceMapping(mapping);
            await Database.SaveChangesAsync();
        }
    }
}
