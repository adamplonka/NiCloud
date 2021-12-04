using NUnit.Framework;
using System.Net.Http;
using System.Threading.Tasks;
using System.Threading;
using System;
using NiCloud;
using System.Collections.Concurrent;
using System.Linq;
using System.Text.Json;
using System.Net;

namespace NiCloudTests
{
    record Record(int A, int B);

    public class Tests
    {
        [Test]
        public void CookieSerializationTest()
        {
            var container = new CookieContainer();
            container.Add(new Cookie("name", "value", "/", "domain.com"));
            var session = new NiCloudSession
            {
                Cookies = container
            };

            var serialized = JsonSerializer.Serialize(session, JsonSerializeOptions.CamelCaseProperties);
            StringAssert.Contains("domain.com", serialized);
        }

        [Ignore(reason: "in development")]
        public void Test1()
        {
            var handler = new MockMessageHandler(async request =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent((await request.Content.ReadAs<Record>()).A.ToString())
                }
            );

            var session = new NiCloudSession();
            session.Cookies.Add(new Cookie("abc", "cookie value", "/", "test.pl"));
            session.AuthHeaders["a"] = "b";
            session.AuthHeaders["b"] = "b";
            session.AuthHeaders["c"] = "b";
            var serialized = JsonSerializer.Serialize(session, JsonSerializeOptions.CamelCaseProperties);
            var newSession = JsonSerializer.Deserialize<NiCloudSession>(serialized, JsonSerializeOptions.CamelCaseProperties);
        }
    }


    public class MockMessageHandler : HttpMessageHandler
    {
        ConcurrentBag<Func<HttpRequestMessage, Task<HttpResponseMessage>>> handlers = new();

        public MockMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            this.handlers.Add(handler);
        }

        public MockMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            this.handlers.Add(request => Task.FromResult(handler(request)));
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {

            var response = await handlers.First()(request);
            return response;
        }
    }
}