using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NiCloud.Services;

public static partial class NiCloudServiceExtensions
{
    public static FilesService Files(this NiCloudService service)
    {
        return new FilesService(service.GetWebServiceUrl("ubiquity"), service);
    }
}

public class FilesService
{
    readonly string serviceRoot;
    readonly NiCloudService service;
    FileNode root;

    public FilesService(string serviceRoot, NiCloudService service)
    {
        this.serviceRoot = serviceRoot;
        this.service = service;
    }

    /// Returns a node URL.
    string GetNodeUrl(string nodeId, string variant = "item")
    {
        return $"{serviceRoot}/ws/{service.Dsid}/{variant}/{nodeId}";
    }

    public async Task<FileNode> GetNode(string nodeId)
    {
        var response = await service.Get(GetNodeUrl(nodeId));
        var s = response.ReadAsStringAsync();
        return new FileNode();
    }

    public record Node(object[] Item_list);

    public async Task<FileNode[]> GetChildren(string nodeId)
    {
        var response = await service.Get(GetNodeUrl(nodeId, "parent"));
        var s = await response.ReadAsStringAsync();
        var node = await response.ReadAs<Node>();
        return node.Item_list.Select(item => new FileNode(item)).ToArray();
    }

    public async Task<Stream> GetFile(string nodeId)
    {
        var response = await service.Get(GetNodeUrl(nodeId, "file"));
        return await response.ReadAsStreamAsync();
    }
}

public class FileNode
{
    private object item;

    public FileNode(object item = null)
    {
        this.item = item;
    }
}
