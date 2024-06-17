namespace Chronofoil.Web.Auth;

public interface IAuthListener
{
    public void Register();
    public void Login();

    public void Error(string errorText);
}