using Chronofoil.Web.Info;
using Dalamud.Bindings.ImGui;

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
                ImGui.TextWrapped(qa.Answer);
    }
}