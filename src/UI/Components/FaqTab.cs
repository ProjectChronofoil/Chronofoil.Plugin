using System.Linq;
using Chronofoil.Common.Info;
using Chronofoil.Utility;
using Chronofoil.Web.Info;
using Dalamud.Interface.Utility;
using ImGuiNET;

namespace Chronofoil.UI.Components;

public class FaqTab
{
    private readonly InfoService _infoService;
    
    public FaqTab(InfoService infoService)
    {
        _infoService = infoService;
    }

    public void Draw()
    {
        if (_infoService.GetFaq().Entries.Count == 0)
        {
            ImGui.TextUnformatted("Loading FAQ...");
            return;
        }
        
        foreach (var qa in _infoService.GetFaq().Entries)
            if (ImGui.CollapsingHeader(qa.Question))
                ImGuiHelpers.SafeTextWrapped(qa.Answer);
    }
}