using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using static NiCloud.JsonSerializeOptions;

namespace NiCloud;

public static class HttpContentExtensions
{
    public static async Task<T> ReadAs<T>(this HttpContent httpContent)
    {
        return await httpContent.ReadFromJsonAsync<T>(CamelCaseProperties);
    }
}
