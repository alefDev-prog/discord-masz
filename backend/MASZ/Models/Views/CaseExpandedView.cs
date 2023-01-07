using System.Text.RegularExpressions;
using Discord;
using MASZ.Models.Views;

namespace MASZ.Models
{
    public class CaseExpandedView
    {
        public CaseExpandedView(
            ModCase modCase,
            IUser moderator,
            IUser lastModerator,
            IUser suspect,
            List<CommentExpandedView> comments,
            UserNoteExpandedView userNoteView,
            Dictionary<string, string> mentionedUsers,
            Dictionary<string, string> mentionedRoles,
            Dictionary<string, string> mentionedChannels
        )
        {
            ModCase = new CaseView(modCase);
            Moderator = DiscordUserView.CreateOrDefault(moderator);
            LastModerator = DiscordUserView.CreateOrDefault(lastModerator);
            Suspect = DiscordUserView.CreateOrDefault(suspect);
            Comments = comments;
            UserNote = userNoteView;
            LinkedCases = new List<CaseView>();
            LinkedEvidence = modCase.EvidenceMappings.Select(x => new VerifiedEvidenceView(x.Evidence)).ToList();

            MentionedUsers = mentionedUsers;
            MentionedRoles = mentionedRoles;
            MentionedChannels = mentionedChannels;

            if (modCase.PunishedUntil != null)
            {
                if (modCase.PunishedUntil > modCase.CreatedAt)
                {
                    if (modCase.PunishedUntil < DateTime.UtcNow)
                    {
                        PunishmentProgress = 100;
                    }
                    else
                    {
                        double totalPunished = (modCase.PunishedUntil.Value - modCase.CreatedAt).TotalSeconds;
                        double alreadyPunished = (DateTime.UtcNow - modCase.CreatedAt).TotalSeconds;

                        PunishmentProgress = alreadyPunished / totalPunished * 100;
                    }
                }
            }
        }
        public CaseView ModCase { get; set; }
        public List<CaseView> LinkedCases { get; set; }
        public List<VerifiedEvidenceView> LinkedEvidence { get; set; }
        public DiscordUserView Moderator { get; set; }
        public DiscordUserView LastModerator { get; set; }
        public DiscordUserView Suspect { get; set; }
        public DiscordUserView LockedBy { get; set; }
        public DiscordUserView DeletedBy { get; set; }
        public List<CommentExpandedView> Comments { get; set; }
        public UserNoteExpandedView UserNote { get; set; }
        public double? PunishmentProgress { get; set; }
        public Dictionary<string, string> MentionedUsers { get; set; }
        public Dictionary<string, string> MentionedRoles { get; set; }
        public Dictionary<string, string> MentionedChannels { get; set; }

        public void RemoveModeratorInfo()
        {
            Moderator = null;
            LastModerator = null;
            LockedBy = null;
            DeletedBy = null;
            LinkedCases.Clear();
            ModCase.RemoveModeratorInfo();

            foreach (var comment in Comments)
            {
                comment.RemoveModeratorInfo(ModCase.UserId);
            }
        }
    }
}
