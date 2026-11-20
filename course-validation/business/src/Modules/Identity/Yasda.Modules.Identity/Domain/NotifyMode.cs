namespace Yasda.Modules.Identity.Domain;

// Who hears about work nobody has taken yet (decision D9). A team's leads are its members whose role is TeamLead, and
// they always hear; this says who else does.
internal enum NotifyMode
{
    LeadsOnly,
    LeadsAndSelected,
    Everyone,
}
