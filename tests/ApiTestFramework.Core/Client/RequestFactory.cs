using RestSharp;

namespace ApiTestFramework.Core.Client;

/// <summary>
/// Static helper to quickly build common request types.
/// </summary>
public static class RequestFactory
{
    public static RequestBuilder Get(string path) =>
        RequestBuilder.Create().WithMethod(Method.Get).WithPath(path);

    public static RequestBuilder Post(string path, object body) =>
        RequestBuilder.Create().WithMethod(Method.Post).WithPath(path).WithBody(body);

    public static RequestBuilder Put(string path, object body) =>
        RequestBuilder.Create().WithMethod(Method.Put).WithPath(path).WithBody(body);

    public static RequestBuilder Delete(string path) =>
        RequestBuilder.Create().WithMethod(Method.Delete).WithPath(path);

    public static RequestBuilder Patch(string path, object body) =>
        RequestBuilder.Create().WithMethod(Method.Patch).WithPath(path).WithBody(body);
}
