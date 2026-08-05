using System.Net;
using System.Text;
using DocumentationGenerator.Application.Configuration;
using DocumentationGenerator.Domain.Models;
using DocumentationGenerator.Infrastructure.Ollama;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DocumentationGenerator.Tests;

public sealed class OllamaTests
{
    [Fact]
    public void Json_Cleanup_Removes_Fences_And_Surrounding_Text()
    {
        var result = OllamaJsonParser.Deserialize<UserManual>("Before\n```json\n{\"title\":\"Customers\"}\n```\nAfter");
        Assert.Equal("Customers", result.Title);
    }

    [Fact]
    public void Json_Parser_Normalizes_Array_And_Object_Values_Used_For_String_Properties()
    {
        const string response = """
            {
              "title": "Customer Management User Manual",
              "notes": "Review generated content",
              "tables": [{
                "name": "Customers",
                "sorting": ["Customer ID", "Customer Name"],
                "filtering": { "fields": ["Search", "Status"] },
                "pagination": { "enabled": true, "pageSizes": [10, 25, 50] }
              }]
            }
            """;

        var manual = OllamaJsonParser.Deserialize<UserManual>(response);

        Assert.Equal(["Review generated content"], manual.Notes);
        Assert.Equal("Customer ID; Customer Name", manual.Tables[0].Sorting);
        Assert.Equal("Search; Status", manual.Tables[0].Filtering);
        Assert.Equal("enabled: true; pageSizes: 10; 25; 50", manual.Tables[0].Pagination);
    }

    [Fact]
    public async Task Invalid_Json_Is_Repaired_Once()
    {
        var handler = new QueueHandler(
            Json("{\"models\":[{\"name\":\"writer:latest\",\"size\":1}]}"),
            Json("{\"message\":{\"content\":\"not json\"}}"),
            Json("{\"message\":{\"content\":\"{\\\"title\\\":\\\"Repaired manual\\\"}\"}}"));
        var service = CreateService(handler);

        var manual = await service.GenerateJsonAsync<UserManual>("writer", "prompt");

        Assert.Equal("Repaired manual", manual.Title);
        Assert.Equal(3, handler.CallCount);
    }

    [Fact]
    public async Task Invalid_Json_After_Repair_Returns_Clear_Error()
    {
        var handler = new QueueHandler(
            Json("{\"models\":[{\"name\":\"writer\",\"size\":1}]}"),
            Json("{\"message\":{\"content\":\"bad one\"}}"),
            Json("{\"message\":{\"content\":\"bad two\"}}"));
        var service = CreateService(handler);

        var exception = await Assert.ThrowsAsync<OllamaResponseException>(() =>
            service.GenerateJsonAsync<UserManual>("writer", "prompt"));

        Assert.Contains("after one repair attempt", exception.Message);
        Assert.Equal(3, handler.CallCount);
    }

    private static OllamaService CreateService(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434/") };
        var options = Options.Create(new OllamaOptions());
        return new OllamaService(client, options, NullLogger<OllamaService>.Instance);
    }

    private static HttpResponseMessage Json(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json")
    };

    private sealed class QueueHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(_responses.Dequeue());
        }
    }
}
