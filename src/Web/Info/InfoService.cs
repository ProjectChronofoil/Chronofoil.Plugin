using System.Threading.Tasks;
using Chronofoil.Common.Info;

namespace Chronofoil.Web.Info;

public class InfoService
{
    private FaqResponse? _faq = null;
    
    private readonly ChronofoilClient _client;

    public InfoService(ChronofoilClient client)
    {
        _client = client;
    }

    public TosResponse GetTos()
    {
        return _client.GetTos();
    }

    public FaqResponse GetFaq()
    {
        if (_faq == null)
        {
            _faq = new FaqResponse();
            Task.Run(() => _client.GetFaq()).ContinueWith(task => task.Exception == null ? _faq = task.Result : _faq = new FaqResponse());
        }
        return _faq;
    }
}