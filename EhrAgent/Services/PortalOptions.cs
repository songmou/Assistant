namespace EhrAgent.Services;

public sealed class PortalOptions
{
    public const string SectionName = "Portal";
    public string EntryUrl { get; set; } = "https://atrust01.chowtaiseng.com/ac_portal/homepage/index.html#/index";
    public bool Headless { get; set; } = true;
}
