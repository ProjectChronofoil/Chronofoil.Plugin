using Newtonsoft.Json;

namespace Chronofoil.Utility;

public static class JwtReader
{
    public static (string userName, string provider) GetTokenInfo(string token)
    {
        var (header, payload, verification) = JWTDecoder.Decoder.DecodeToken(token);
        var tokenObject = JsonConvert.DeserializeObject<Token>(payload);
        return (tokenObject.UniqueName, tokenObject.AuthProvider);
    }
}

internal class Token
{
    public string NameId { get; set; }
    [JsonProperty(PropertyName = "unique_name")]
    public string UniqueName { get; set; }
    public string AuthProvider { get; set; }
}