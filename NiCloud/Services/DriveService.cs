using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace NiCloud.Services;

public static partial class NiCloudServiceExtensions
{
    public static DriveService Drive(this NiCloudService service)
    {
        return new DriveService(service, service.GetWebServiceUrl("drivews"), service.GetWebServiceUrl("docws"));
    }
}

public class DriveNode
{
    readonly DriveService service;
    NodeData nodeData;

    public string DrivewsId => nodeData.Drivewsid;
    public string DocwsId => nodeData.Docwsid;

    public DriveNode(DriveService service, NodeData nodeData)
    {
        this.service = service;
        this.nodeData = nodeData;
        this.Name = string.IsNullOrEmpty(nodeData.Extension)
            ? nodeData.Name
            : $"{nodeData.Name}.{nodeData.Extension}";
    }

    public string Name { get; }

    public NodeType Type => nodeData.Type;

    public long Size => nodeData.Size;

    /// <summary>
    /// Gets the node children.
    /// </summary>
    public async Task<IEnumerable<DriveNode>> GetChildren(bool loadDescendants = false)
    {
        if (Type != NodeType.Folder)
        {
            return Enumerable.Empty<DriveNode>();
        }

        if (nodeData.Items == null)
        {
            Console.WriteLine("No items, Updating node data");
            nodeData = await service.GetNodeData(nodeData.Drivewsid);
        }

        var children = nodeData.Items;
        if (children == null)
        {
            return Enumerable.Empty<DriveNode>();
        }

        if (loadDescendants)
        {
            var folderDrivewsids = children
                .Where(child => child.Type == NodeType.Folder)
                .Select(child => child.Drivewsid);
            var folderChildrenData = await service.GetNodesData(folderDrivewsids);
            return folderChildrenData
                .Concat(children.Where(child => child.Type != NodeType.Folder))
                .Select(child => new DriveNode(service, child));
        }
        return children.Select(child => new DriveNode(service, child));
    }

    /// <summary>
    /// Moves an iCloud Drive node to the trash bin
    /// </summary>
    public async Task<NodeData> Delete()
    {
        var response = await service.Delete(new[] { nodeData });
        return response.FirstOrDefault();
    }
}

public enum NodeType
{
    Unknown,
    File,
    Folder
}

public class NodeData
{
    public DateTime DateCreated { get; init; }
    public DateTime DateChanged { get; init; }
    public DateTime DateModified { get; init; }
    public string Drivewsid { get; init; }
    public string Docwsid { get; init; }
    public string Zone { get; init; }
    public string Name { get; init; }
    public string Extension { get; init; }
    public long Size { get; init; }
    public string Etag { get; init; }
    public string ClientId { get; init; }

    [JsonPropertyName("type")]
    public string TypeRaw { get; init; }

    [JsonIgnore]
    public NodeType Type => Enum.TryParse<NodeType>(TypeRaw, true, out var type) ? type : NodeType.Unknown;
    public long AssetQuota { get; init; }
    public int FileCount { get; init; }
    public int ShareCount { get; init; }
    public int ShareAliasCount { get; init; }
    public int DirectChildrenCount { get; init; }
    public NodeData[] Items { get; init; }
    public int NumberOfItems { get; init; }
    public string Status { get; init; }
    public NodeData[] Hierarchy { get; init; }
    public bool IsDeleted { get; init; } = false;
}

public record GetNodeDataRequest(string Drivewsid, bool PartialData = false, bool IncludeHierarchy = false);

/// <summary>
/// The Drive iCloud service.
/// </summary>
public class DriveService
{
    readonly NiCloudService service;
    readonly string serviceRoot;
    readonly string documentRoot;
    DriveNode root;

    public DriveService(NiCloudService service, string serviceRoot, string documentRoot)
    {
        this.service = service;
        this.serviceRoot = serviceRoot;
        this.documentRoot = documentRoot;
    }

    public async Task<DriveNode> GetRoot()
    {
        if (root != null)
        {
            return root;
        }

        return root ??= new DriveNode(this, await GetNodeData("FOLDER::com.apple.CloudDocs::root"));
    }

    public async Task<NodeData[]> GetNodesData(IEnumerable<string> drivewsids)
    {
        var response = await service.Post(serviceRoot + "/retrieveItemDetailsInFolders",
            drivewsids.Select(drivewsid => new GetNodeDataRequest(drivewsid, false, true)));
        var data = await response.ReadAs<NodeData[]>();
        return data;
    }


    public async Task<NodeData> GetNodeData(string nodeId)
    {
        return (await GetNodesData(new[] { nodeId })).FirstOrDefault();
    }

    public record NodeId(string Drivewsid, string Etag, string ClientId = null);

    public async Task<NodeData[]> Delete(IEnumerable<NodeData> nodes)
    {
        var request = new
        {
            Items = nodes.Select(nodeData => new NodeId(nodeData.Drivewsid, nodeData.Etag, nodeData.ClientId))
        };
        var response = await service.Post(serviceRoot + "/moveItemsToTrash", request);
        return await response.ReadAs<NodeData[]>();
    }

    public async Task<NodeData[]> DeleteFromTrash(IEnumerable<NodeData> nodes)
    {
        var request = new
        {
            Items = nodes.Select(nodeData => new NodeId(nodeData.Drivewsid, nodeData.Etag))
        };
        var response = await service.Post(serviceRoot + "/deleteItemsInTrash", request);
        return await response.ReadAs<NodeData[]>();
    }

    public async Task<NodeData[]> MoveTo(IEnumerable<NodeData> nodes, string destinationDriveWsid)
    {
        var request = new
        {
            DestinationDrivewsId = destinationDriveWsid,
            Items = nodes.Select(nodeData => new NodeId(nodeData.Drivewsid, nodeData.Etag, nodeData.ClientId))
        };
        var response = await service.Post(serviceRoot + "/moveItems", request);
        return await response.ReadAs<NodeData[]>();
    }


    public class DownloadInfo
    {
        public string Document_id { get; init; }
        public long Owner_dsid { get; init; }
        public DataToken Data_token { get; init; }
        public string Double_etag { get; init; }
    }

    public class DataToken
    {
        public string Url { get; init; }
        public string Token { get; init; }
        public string Signature { get; init; }
        public string Wrapping_key { get; init; }
        public string Reference_signature { get; init; }
    }

    public async Task<DownloadInfo[]> GetDownloadInfo(IEnumerable<DriveNode> nodes)
    {
        var request = nodes.Select(nodeData => new { Document_id = nodeData.DocwsId });
        var response = await service.Post(documentRoot + "/ws/com.apple.CloudDocs/download/batch?token=" + service.GetAuthToken(), request);

        return await response.ReadAs<DownloadInfo[]>();
    }

    public async Task<Stream> DownloadFile(DriveNode node)
    {
        var downloadInfo = (await GetDownloadInfo(new[] { node })).FirstOrDefault();
        return await DownloadFile(downloadInfo);
    }

    public async Task<Stream> DownloadFile(DownloadInfo downloadInfo)
    {
        var url = downloadInfo?.Data_token?.Url;
        var fileResponse = await service.Get(url);
        return fileResponse.ReadAsStream();
    }

    public class FolderId
    {
        public string Name { get; init; }
        public string ClientId { get; init; }

        public FolderId(string name)
        {
            Name = name;
            var guid = Guid.NewGuid().ToString()[..^4].ToLowerInvariant();
            ClientId = $"FOLDER::{guid}::{guid}";
        }
    }

    public record MakeDirectoryResponse
    {
        public string DestinationDrivewsId { get; init; }
        public NodeData[] Folders { get; init; }
    }

    public async Task<NodeData> MakeDirectory(string name, string parentDrivewsid)
    {
        var request = new
        {
            destinationDrivewsId = parentDrivewsid,
            folders = new[] {
                new FolderId(name)
            }
        };
        var response = await service.Post(serviceRoot + "/createFolders?appIdentifier=iclouddrive&clientId=" + service.ClientId + "&dsid=" + service.Dsid,
            request);
        var foldersCreated = (await response.ReadAs<MakeDirectoryResponse>())?.Folders;
        return foldersCreated?.FirstOrDefault();
    }

    /*

def __init__(self, service_root, document_root, session, params):
   self._service_root = service_root
   self._document_root = document_root
   self.session = session
   self.params = dict(params)
   self._root = None

def _get_token_from_cookie(self):
   for cookie in self.session.cookies:
       if cookie.name == "X-APPLE-WEBAUTH-VALIDATE":
           match = search(r"\bt=([^:]+)", cookie.value)
           if match is None:
               raise Exception("Can't extract token from %r" % cookie.value)
           return {"token": match.group(1)}
   raise Exception("Token cookie not found")

def get_node_data(self, node_id):
   """Returns the node data."""
   request = self.session.post(
       self._service_root + "/retrieveItemDetailsInFolders",
       params=self.params,
       data=json.dumps(
           [
               {
                   "drivewsid": "FOLDER::com.apple.CloudDocs::%s" % node_id,
                   "partialData": False,
               }
           ]
       ),
   )
   return request.json()[0]

def get_file(self, file_id, **kwargs):
   """Returns iCloud Drive file."""
   file_params = dict(self.params)
   file_params.update({"document_id": file_id})
   response = self.session.get(
       self._document_root + "/ws/com.apple.CloudDocs/download/by_id",
       params=file_params,
   )
   if not response.ok:
       return None
   url = response.json()["data_token"]["url"]
   return self.session.get(url, params=self.params, **kwargs)

def get_app_data(self):
   """Returns the app library (previously ubiquity)."""
   request = self.session.get(
       self._service_root + "/retrieveAppLibraries", params=self.params
   )
   return request.json()["items"]

def _get_upload_contentws_url(self, file_object):
   """Get the contentWS endpoint URL to add a new file."""
   content_type = mimetypes.guess_type(file_object.name)[0]
   if content_type is None:
       content_type = ""

   # Get filesize from file object
   orig_pos = file_object.tell()
   file_object.seek(0, os.SEEK_END)
   file_size = file_object.tell()
   file_object.seek(orig_pos, os.SEEK_SET)

   file_params = self.params
   file_params.update(self._get_token_from_cookie())

   request = self.session.post(
       self._document_root + "/ws/com.apple.CloudDocs/upload/web",
       params=file_params,
       headers={"Content-Type": "text/plain"},
       data=json.dumps(
           {
               "filename": file_object.name,
               "type": "FILE",
               "content_type": content_type,
               "size": file_size,
           }
       ),
   )
   if not request.ok:
       return None
   return (request.json()[0]["document_id"], request.json()[0]["url"])

def _update_contentws(self, folder_id, sf_info, document_id, file_object):
   data = {
       "data": {
           "signature": sf_info["fileChecksum"],
           "wrapping_key": sf_info["wrappingKey"],
           "reference_signature": sf_info["referenceChecksum"],
           "size": sf_info["size"],
       },
       "command": "add_file",
       "create_short_guid": True,
       "document_id": document_id,
       "path": {"starting_document_id": folder_id, "path": file_object.name,},
       "allow_conflict": True,
       "file_flags": {
           "is_writable": True,
           "is_executable": False,
           "is_hidden": False,
       },
       "mtime": int(time.time() * 1000),
       "btime": int(time.time() * 1000),
   }

   # Add the receipt if we have one. Will be absent for 0-sized files
   if sf_info.get("receipt"):
       data["data"].update({"receipt": sf_info["receipt"]})

   request = self.session.post(
       self._document_root + "/ws/com.apple.CloudDocs/update/documents",
       params=self.params,
       headers={"Content-Type": "text/plain"},
       data=json.dumps(data),
   )
   if not request.ok:
       return None
   return request.json()

def send_file(self, folder_id, file_object):
   """Send new file to iCloud Drive."""
   document_id, content_url = self._get_upload_contentws_url(file_object)

   request = self.session.post(content_url, files={file_object.name: file_object})
   if not request.ok:
       return None
   content_response = request.json()["singleFile"]

   self._update_contentws(folder_id, content_response, document_id, file_object)

def create_folders(self, parent, name):
   """Creates a new iCloud Drive folder"""
   request = self.session.post(
       self._service_root + "/createFolders",
       params=self.params,
       headers={"Content-Type": "text/plain"},
       data=json.dumps(
           {
               "destinationDrivewsId": parent,
               "folders": [{"clientId": self.params["clientId"], "name": name,}],
           }
       ),
   )
   return request.json()

def rename_items(self, node_id, etag, name):
   """Renames an iCloud Drive node"""
   request = self.session.post(
       self._service_root + "/renameItems",
       params=self.params,
       data=json.dumps(
           {"items": [{"drivewsid": node_id, "etag": etag, "name": name,}],}
       ),
   )
   return request.json()

def move_items_to_trash(self, node_id, etag):
   """Moves an iCloud Drive node to the trash bin"""
   request = self.session.post(
       self._service_root + "/moveItemsToTrash",
       params=self.params,
       data=json.dumps(
           {
               "items": [
                   {
                       "drivewsid": node_id,
                       "etag": etag,
                       "clientId": self.params["clientId"],
                   }
               ],
           }
       ),
   )
   return request.json()

@property
def root(self):
   """Returns the root node."""
   if not self._root:
       self._root = DriveNode(self, self.get_node_data("root"))
   return self._root

def __getattr__(self, attr):
   return getattr(self.root, attr)

def __getitem__(self, key):
   return self.root[key]


class DriveNode(object):
"""Drive node."""

def __init__(self, conn, data):
   self.data = data
   self.connection = conn
   self._children = None

@property
def name(self):
   """Gets the node name."""
   if "extension" in self.data:
       return "%s.%s" % (self.data["name"], self.data["extension"])
   return self.data["name"]

@property
def type(self):
   """Gets the node type."""
   node_type = self.data.get("type")
   return node_type and node_type.lower()

def get_children(self):
   """Gets the node children."""
   if not self._children:
       if "items" not in self.data:
           self.data.update(self.connection.get_node_data(self.data["docwsid"]))
       if "items" not in self.data:
           raise KeyError("No items in folder, status: %s" % self.data["status"])
       self._children = [
           DriveNode(self.connection, item_data)
           for item_data in self.data["items"]
       ]
   return self._children

@property
def size(self):
   """Gets the node size."""
   size = self.data.get("size")  # Folder does not have size
   if not size:
       return None
   return int(size)

@property
def date_changed(self):
   """Gets the node changed date (in UTC)."""
   return _date_to_utc(self.data.get("dateChanged"))  # Folder does not have date

@property
def date_modified(self):
   """Gets the node modified date (in UTC)."""
   return _date_to_utc(self.data.get("dateModified"))  # Folder does not have date

@property
def date_last_open(self):
   """Gets the node last open date (in UTC)."""
   return _date_to_utc(self.data.get("lastOpenTime"))  # Folder does not have date

def open(self, **kwargs):
   """Gets the node file."""
   # iCloud returns 400 Bad Request for 0-byte files
   if self.data["size"] == 0:
       response = Response()
       response.raw = io.BytesIO()
       return response
   return self.connection.get_file(self.data["docwsid"], **kwargs)

def upload(self, file_object, **kwargs):
   """"Upload a new file."""
   return self.connection.send_file(self.data["docwsid"], file_object, **kwargs)

def dir(self):
   """Gets the node list of directories."""
   if self.type == "file":
       return None
   return [child.name for child in self.get_children()]

def mkdir(self, folder):
   """Create a new directory directory."""
   return self.connection.create_folders(self.data["drivewsid"], folder)

def rename(self, name):
   """Rename an iCloud Drive item."""
   return self.connection.rename_items(
       self.data["drivewsid"], self.data["etag"], name
   )

def delete(self):
   """Delete an iCloud Drive item."""
   return self.connection.move_items_to_trash(
       self.data["drivewsid"], self.data["etag"]
   )

def get(self, name):
   """Gets the node child."""
   if self.type == "file":
       return None
   return [child for child in self.get_children() if child.name == name][0]

def __getitem__(self, key):
   try:
       return self.get(key)
   except IndexError:
       raise KeyError("No child named '%s' exists" % key)

def __unicode__(self):
   return "{type: %s, name: %s}" % (self.type, self.name)

def __str__(self):
   as_unicode = self.__unicode__()
   if PY2:
       return as_unicode.encode("utf-8", "ignore")
   return as_unicode

def __repr__(self):
   return "<%s: %s>" % (type(self).__name__, str(self))
*/
}
