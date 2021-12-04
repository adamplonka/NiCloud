using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace NiCloud.Services;

public static partial class NiCloudServiceExtensions
{
    public static PhotosService Photos(this NiCloudService service)
    {
        return new PhotosService(service.GetWebServiceUrl("ckdatabasews"), service);
    }
}

record SmartFolder(string Obj_type, string List_type, string Direction, QueryFilter[]? Query_filter);

public record QueryFilter(string FieldName, Field FieldValue, string Comparator = "EQUALS");

public class PhotoAlbum
{
    public string Name { get; init; }
    public string ListType { get; private set; }
    public string FolderObjType { get; private set; }
    public string Direction { get; private set; }
    public QueryFilter[] QueryFilter { get; private set; }

    private readonly SmartFolder properties;
    readonly PhotosService service;

    internal PhotoAlbum(PhotosService service, string folderName, SmartFolder properties)
        : this(service, folderName, properties.List_type, properties.Obj_type, properties.Direction, properties.Query_filter)
    {
    }

    public PhotoAlbum(PhotosService service, string folderName, string listType, string folderObjType, string direction, QueryFilter[] queryFilter)
    {
        this.service = service;
        this.Name = folderName;
        this.ListType = listType;
        this.FolderObjType = folderObjType;
        this.Direction = direction;
        this.QueryFilter = queryFilter;
    }

    /// <summary>
    /// Returns the album photos.
    /// </summary>
    public IAsyncEnumerable<PhotoAsset[]> GetPhotos(int startAt = 0)
    {
        return service.GetPhotos(this, startAt);
    }
}

public class PhotoAsset
{
    public PhotoAsset()
    { }

    public PhotoAsset(PhotosService photosService, Record masterRecord, IReadOnlyCollection<Record> assetRecords)
    {
        PhotosService = photosService;
        MasterRecord = masterRecord;
        AssetRecords = assetRecords;
        Original = MasterRecord.Fields.GetValueOrDefault("resOriginalRes")?.GetConvertedValue() as AssetId;
        JpegFull = AssetRecords
            .Select(assetRecord => assetRecord.Fields.TryGetValue("resJPEGFullRes", out Field field) ? field : null)
            .FirstOrDefault(field => field != null)
            ?.GetConvertedValue() as AssetId;
        LivePhoto = MasterRecord.Fields.GetValueOrDefault("resOriginalVidComplRes")?.GetConvertedValue() as AssetId;
    }

    [JsonIgnore]
    public PhotosService PhotosService { get; }
    public Record MasterRecord { get; init; }
    public IReadOnlyCollection<Record> AssetRecords { get; init;  }
    public AssetId Original { get; init; }
    public AssetId OriginalAlt => MasterRecord.Fields.GetValueOrDefault("resOriginalAltRes")?.GetConvertedValue() as AssetId;

    public string FileName => MasterRecord.Fields.GetValueOrDefault("filenameEnc")?.GetConvertedValue() as string;

    public long Size => Original?.Size ?? 0;


    public AssetId JpegFull { get; init; }

    public AssetId LivePhoto { get; init; }

    public DateTime CreateDate => AssetRecords.First().Fields.GetValueOrDefault("assetDate")?.GetConvertedValue() as DateTime? ?? default;
}

public class PhotosService
{
    static readonly Dictionary<string, SmartFolder> SMART_FOLDERS = new()
    {
        ["All Photos"] = new SmartFolder(
            "CPLAssetByAssetDateWithoutHiddenOrDeleted",
            "CPLAssetAndMasterByAssetDateWithoutHiddenOrDeleted",
            "ASCENDING",
            null
        )
    };

    public string ServiceEndpoint { get; init; }
    readonly string serviceRoot;
    readonly NiCloudService service;

    public PhotosService(string serviceRoot, NiCloudService service)
    {
        this.serviceRoot = serviceRoot;
        this.service = service;
        this.ServiceEndpoint = $"{serviceRoot}/database/1/com.apple.photos.cloud/production/private";
    }

    public async Task Init()
    {
        var url = $"{ServiceEndpoint}/records/query?remapEnums=true&getCurrentSyncToken=true";
        var data = new QueryRequest("CheckIndexingState");
        var response = await service.Post(url, data);
        var queryResponse = await response.ReadAs<QueryResponse>();

        var indexingState = queryResponse.Records?.FirstOrDefault()?.Fields.TryGetValue("state", out Field field) == true
            ? field.Value.ToString()
            : null;

        if (indexingState != "FINISHED")
        {
            throw new NiCloudServiceNotActivatedException("iCloud Photo Library not finished indexing. " +
                "Please try again in a few minutes.");
        }
        //self._photo_assets = {}*/
    }

    public async Task<PhotoAlbum[]> Albums()
    {
        var albums = new List<PhotoAlbum>();
        foreach (var (name, smartFolder) in SMART_FOLDERS)
        {
            albums.Add(new PhotoAlbum(this, name, smartFolder));
        }

        foreach (var folder in await FetchFolders())
        {
            if (!folder.Fields.ContainsKey("albumNameEnc"))
            {
                continue;
            }

            if (folder.RecordName == "----Root-Folder----" || folder.Deleted)
            {
                continue;
            }

            var folderId = folder.RecordName;
            var folderObjType = $"CPLContainerRelationNotDeletedByAssetDate:{folderId}";
            var folderName = folder.Fields["albumNameEnc"].GetConvertedValue() as string;

            var queryFilter = new QueryFilter[] {
                new("parentId", new("STRING", folderId), "EQUALS")
            };

            var album = new PhotoAlbum(
                this,
                folderName,
                "CPLContainerRelationLiveByAssetDate",
                folderObjType,
                "ASCENDING",
                queryFilter
            );
            albums.Add(album);
        }

        return albums.ToArray();
    }


    public async Task<Record[]> FetchFolders()
    {
        var url = $"{ServiceEndpoint}/records/query?remapEnums=true&getCurrentSyncToken=true&dsid={service.Dsid}";
        var data = new QueryRequest("CPLAlbumByPositionLive"); // CPLAssetByAssetDateWithoutHiddenOrDeleted

        var response = await service.Post(url, data);
        var result = await response.ReadAs<QueryResponse>();

        return result.Records;
    }

    /// <summary>
    /// Returns the album photos.
    /// </summary>
    public async IAsyncEnumerable<PhotoAsset[]> GetPhotos(PhotoAlbum photoAlbum, int startAt = 0)
    {
        /*
        if self.direction == "DESCENDING":
            offset = len(self) - 1
        else:
            offset = 0
        */
        var offset = startAt;
        while (true)
        {
            var url = $"{ServiceEndpoint}/records/query?remapEnums=true&getCurrentSyncToken=true&dsid={service.Dsid}";
            using var request = await service.Post(url, GenerateQuery(offset, photoAlbum.ListType, photoAlbum.Direction, photoAlbum.QueryFilter));
            var response = await request.ReadAs<QueryResponse>();

            Dictionary<string, List<Record>> assetRecords = new();
            List<Record> masterRecords = new();
            foreach (var rec in response.Records)
            {
                if (rec.RecordType == "CPLAsset")
                {
                    var masterId = (rec.Fields["masterRef"].GetConvertedValue() as RecordReference)?.RecordName;
                    if (!assetRecords.TryGetValue(masterId, out var records))
                    {
                        assetRecords[masterId] = records = new List<Record>();
                    }
                    records.Add(rec);
                }
                else if (rec.RecordType == "CPLMaster")
                {
                    masterRecords.Add(rec);
                }
            }

            if (masterRecords.Count == 0)
            {
                break;
            }
            offset +=  100;
            //    if self.direction == "DESCENDING":
            //    offset = offset - master_records_len
            //else:
            //    offset = offset + master_records_len


            List<PhotoAsset> chunk = new();
            foreach (var record in masterRecords)
            {
                var recordName = record.RecordName;
                var records = assetRecords.GetOrDefault(recordName);
                chunk.Add(new PhotoAsset(this, record, records));
            }
            yield return chunk.ToArray();
        }
    }


    public QueryRequest GenerateQuery(int offset, string listType, string direction, QueryFilter[] queryFilters)
    {
        const int pageSize = 100;
        var query = new QueryRequest
        {
            Query = new(listType)
            {
                FilterBy = (new[] {
                    new QueryFilter("startRank", new("INT64", offset)),
                    new QueryFilter("direction", new("STRING", direction))
                }).Concat(queryFilters ?? Array.Empty<QueryFilter>()).ToArray()
            },
            ResultsLimit = pageSize * 2,
            DesiredKeys = new[]
            {
             "resJPEGFullWidth",
             "resJPEGFullHeight",
             "resJPEGFullFileType",
             "resJPEGFullFingerprint",
             "resJPEGFullRes",
             "resJPEGLargeWidth",
             "resJPEGLargeHeight",
             "resJPEGLargeFileType",
             "resJPEGLargeFingerprint",
             "resJPEGLargeRes",
             "resJPEGMedWidth",
             "resJPEGMedHeight",
             "resJPEGMedFileType",
             "resJPEGMedFingerprint",
             "resJPEGMedRes",
             "resJPEGThumbWidth",
             "resJPEGThumbHeight",
             "resJPEGThumbFileType",
             "resJPEGThumbFingerprint",
             "resJPEGThumbRes",
             "resVidFullWidth",
             "resVidFullHeight",
             "resVidFullFileType",
             "resVidFullFingerprint",
             "resVidFullRes",
             "resVidMedWidth",
             "resVidMedHeight",
             "resVidMedFileType",
             "resVidMedFingerprint",
             "resVidMedRes",
             "resVidSmallWidth",
             "resVidSmallHeight",
             "resVidSmallFileType",
             "resVidSmallFingerprint",
             "resVidSmallRes",
             "resSidecarWidth",
             "resSidecarHeight",
             "resSidecarFileType",
             "resSidecarFingerprint",
             "resSidecarRes",
             "itemType",
             "dataClassType",
             "filenameEnc",
             "originalOrientation",
             "resOriginalWidth",
             "resOriginalHeight",
             "resOriginalFileType",
             "resOriginalFingerprint",
             "resOriginalRes",
             "resOriginalAltWidth",
             "resOriginalAltHeight",
             "resOriginalAltFileType",
             "resOriginalAltFingerprint",
             "resOriginalAltRes",
             "resOriginalVidComplWidth",
             "resOriginalVidComplHeight",
             "resOriginalVidComplFileType",
             "resOriginalVidComplFingerprint",
             "resOriginalVidComplRes",
             "isDeleted",
             "isExpunged",
             "dateExpunged",
             "remappedRef",
             "recordName",
             "recordType",
             "recordChangeTag",
             "masterRef",
             "adjustmentRenderType",
             "assetDate",
             "addedDate",
             "isFavorite",
             "isHidden",
             "orientation",
             "duration",
             "assetSubtype",
             "assetSubtypeV2",
             "assetHDRType",
             "burstFlags",
             "burstFlagsExt",
             "burstId",
             "captionEnc",
             "locationEnc",
             "locationV2Enc",
             "locationLatitude",
             "locationLongitude",
             "adjustmentType",
             "timeZoneOffset",
             "vidComplDurValue",
             "vidComplDurScale",
             "vidComplDispValue",
             "vidComplDispScale",
             "vidComplVisibilityState",
             "customRenderedValue",
             "containerId",
             "itemId",
             "position",
             "isKeyAsset"
            }
        };

        return query;
    }
}

/*
 * {
"records" : [ {
"recordName" : "----Project-Root-Folder----",
"recordType" : "CPLAlbum",
"fields" : {
  "recordModificationDate" : {
    "value" : 1604479018870,
    "type" : "TIMESTAMP"
  },
  "sortAscending" : {
    "value" : 1,
    "type" : "INT64"
  },
  "sortType" : {
    "value" : 0,
    "type" : "INT64"
  },
  "albumType" : {
    "value" : 3,
    "type" : "INT64"
  },
  "position" : {
    "value" : 0,
    "type" : "INT64"
  },
  "sortTypeExt" : {
    "value" : 0,
    "type" : "INT64"
  }
},
"pluginFields" : { },
"recordChangeTag" : "4trx",
"created" : {
  "timestamp" : 1604487709098,
  "userRecordName" : "_fe283de2507f799cd3a1de7c00348a05",
  "deviceID" : "F39FCC192B5883ED4748C2C05AFA509104E1013F561EC3DE429FF77F51C369CD"
},
"modified" : {
  "timestamp" : 1604487709098,
  "userRecordName" : "_fe283de2507f799cd3a1de7c00348a05",
  "deviceID" : "F39FCC192B5883ED4748C2C05AFA509104E1013F561EC3DE429FF77F51C369CD"
},
"deleted" : false,
"zoneID" : {
  "zoneName" : "PrimarySync",
  "ownerRecordName" : "_fe283de2507f799cd3a1de7c00348a05",
  "zoneType" : "REGULAR_CUSTOM_ZONE"
}
}, {
"recordName" : "----Root-Folder----",
"recordType" : "CPLAlbum",
"fields" : {
  "recordModificationDate" : {
    "value" : 1588354625633,
    "type" : "TIMESTAMP"
  },
  "sortAscending" : {
    "value" : 1,
    "type" : "INT64"
  },
  "sortType" : {
    "value" : 0,
    "type" : "INT64"
  },
  "albumType" : {
    "value" : 3,
    "type" : "INT64"
  },
  "position" : {
    "value" : 0,
    "type" : "INT64"
  },
  "sortTypeExt" : {
    "value" : 0,
    "type" : "INT64"
  }
},
"pluginFields" : { },
"recordChangeTag" : "3dhy",
"created" : {
  "timestamp" : 1504919085417,
  "userRecordName" : "_fe283de2507f799cd3a1de7c00348a05",
  "deviceID" : "D4BD5D8AAF15852E345740FF561576ED1B07BA131936558D1391C95E20126C51"
},
"modified" : {
  "timestamp" : 1588355012467,
  "userRecordName" : "_fe283de2507f799cd3a1de7c00348a05",
  "deviceID" : "F39FCC192B5883ED4748C2C05AFA509104E1013F561EC3DE429FF77F51C369CD"
},
"deleted" : false,
"zoneID" : {
  "zoneName" : "PrimarySync",
  "ownerRecordName" : "_fe283de2507f799cd3a1de7c00348a05",
  "zoneType" : "REGULAR_CUSTOM_ZONE"
}
}, {
"recordName" : "6AB9E003-4C2B-494A-A50C-02273204813E",
"recordType" : "CPLAlbum",
"fields" : {
  "recordModificationDate" : {
    "value" : 1504919079526,
    "type" : "TIMESTAMP"
  },
  "sortAscending" : {
    "value" : 1,
    "type" : "INT64"
  },
  "sortType" : {
    "value" : 0,
    "type" : "INT64"
  },
  "albumType" : {
    "value" : 0,
    "type" : "INT64"
  },
  "albumNameEnc" : {
    "value" : "A7OOAKIYZBiLCjMePfz+HB4vRB0skOjfTD9MLjLh",
    "type" : "ENCRYPTED_BYTES"
  },
  "position" : {
    "value" : 7820,
    "type" : "INT64"
  },
  "sortTypeExt" : {
    "value" : 0,
    "type" : "INT64"
  }
},
"pluginFields" : { },
"recordChangeTag" : "6",
"created" : {
  "timestamp" : 1504919085417,
  "userRecordName" : "_fe283de2507f799cd3a1de7c00348a05",
  "deviceID" : "D4BD5D8AAF15852E345740FF561576ED1B07BA131936558D1391C95E20126C51"
},
"modified" : {
  "timestamp" : 1504919085417,
  "userRecordName" : "_fe283de2507f799cd3a1de7c00348a05",
  "deviceID" : "D4BD5D8AAF15852E345740FF561576ED1B07BA131936558D1391C95E20126C51"
},
"deleted" : false,
"zoneID" : {
  "zoneName" : "PrimarySync",
  "ownerRecordName" : "_fe283de2507f799cd3a1de7c00348a05",
  "zoneType" : "REGULAR_CUSTOM_ZONE"
}
}, {
"recordName" : "5C453E78-CB90-4A7D-8903-3633BD93B236",
"recordType" : "CPLAlbum",
"fields" : {
  "recordModificationDate" : {
    "value" : 1504919079526,
    "type" : "TIMESTAMP"
  },
  "sortAscending" : {
    "value" : 1,
    "type" : "INT64"
  },
  "sortType" : {
    "value" : 0,
    "type" : "INT64"
  },
  "albumType" : {
    "value" : 0,
    "type" : "INT64"
  },
  "albumNameEnc" : {
    "value" : "A67YAL9LhxPpZ76prBrYL0JNtv/bUeEHWKD2v7ZTMsdvfaROGhqV9w==",
    "type" : "ENCRYPTED_BYTES"
  },
  "position" : {
    "value" : 15360,
    "type" : "INT64"
  },
  "sortTypeExt" : {
    "value" : 0,
    "type" : "INT64"
  }
},
"pluginFields" : { },
"recordChangeTag" : "7",
"created" : {
  "timestamp" : 1504919085418,
  "userRecordName" : "_fe283de2507f799cd3a1de7c00348a05",
  "deviceID" : "D4BD5D8AAF15852E345740FF561576ED1B07BA131936558D1391C95E20126C51"
},
"modified" : {
  "timestamp" : 1504919085418,
  "userRecordName" : "_fe283de2507f799cd3a1de7c00348a05",
  "deviceID" : "D4BD5D8AAF15852E345740FF561576ED1B07BA131936558D1391C95E20126C51"
},
"deleted" : false,
"zoneID" : {
  "zoneName" : "PrimarySync",
  "ownerRecordName" : "_fe283de2507f799cd3a1de7c00348a05",
  "zoneType" : "REGULAR_CUSTOM_ZONE"
}
}, {
"recordName" : "B6FE3471-EE1A-4CF1-A37E-517022526161",
"recordType" : "CPLAlbum",
"fields" : {
  "recordModificationDate" : {
    "value" : 1504919079526,
    "type" : "TIMESTAMP"
  },
  "sortAscending" : {
    "value" : 1,
    "type" : "INT64"
  },
  "sortType" : {
    "value" : 0,
    "type" : "INT64"
  },
  "albumType" : {
    "value" : 0,
    "type" : "INT64"
  },
  "albumNameEnc" : {
    "value" : "A4TqAIOX8rRw88YfXoPos0ojtgWKVnoy43LNRVnLUM//LWx9CA==",
    "type" : "ENCRYPTED_BYTES"
  },
  "position" : {
    "value" : 16384,
    "type" : "INT64"
  },
  "sortTypeExt" : {
    "value" : 0,
    "type" : "INT64"
  }
},
"pluginFields" : { },
"recordChangeTag" : "9",
"created" : {
  "timestamp" : 1504919085419,
  "userRecordName" : "_fe283de2507f799cd3a1de7c00348a05",
  "deviceID" : "D4BD5D8AAF15852E345740FF561576ED1B07BA131936558D1391C95E20126C51"
},
"modified" : {
  "timestamp" : 1504919085419,
  "userRecordName" : "_fe283de2507f799cd3a1de7c00348a05",
  "deviceID" : "D4BD5D8AAF15852E345740FF561576ED1B07BA131936558D1391C95E20126C51"
},
"deleted" : false,
"zoneID" : {
  "zoneName" : "PrimarySync",
  "ownerRecordName" : "_fe283de2507f799cd3a1de7c00348a05",
  "zoneType" : "REGULAR_CUSTOM_ZONE"
}
}, {
"recordName" : "74FA513E-1C7B-4912-989F-CCED100A3D7E",
"recordType" : "CPLAlbum",
"fields" : {
  "recordModificationDate" : {
    "value" : 1504919079526,
    "type" : "TIMESTAMP"
  },
  "sortAscending" : {
    "value" : 1,
    "type" : "INT64"
  },
  "sortType" : {
    "value" : 0,
    "type" : "INT64"
  },
  "albumType" : {
    "value" : 0,
    "type" : "INT64"
  },
  "albumNameEnc" : {
    "value" : "AzU9AEJEIPddG7CSSDD/94R+nVx71jQlUIhKkFXviC4bnw==",
    "type" : "ENCRYPTED_BYTES"
  },
  "position" : {
    "value" : 17408,
    "type" : "INT64"
  },
  "sortTypeExt" : {
    "value" : 0,
    "type" : "INT64"
  }
},
"pluginFields" : { },
"recordChangeTag" : "c",
"created" : {
  "timestamp" : 1504919085420,
  "userRecordName" : "_fe283de2507f799cd3a1de7c00348a05",
  "deviceID" : "D4BD5D8AAF15852E345740FF561576ED1B07BA131936558D1391C95E20126C51"
},
"modified" : {
  "timestamp" : 1504919085420,
  "userRecordName" : "_fe283de2507f799cd3a1de7c00348a05",
  "deviceID" : "D4BD5D8AAF15852E345740FF561576ED1B07BA131936558D1391C95E20126C51"
},
"deleted" : false,
"zoneID" : {
  "zoneName" : "PrimarySync",
  "ownerRecordName" : "_fe283de2507f799cd3a1de7c00348a05",
  "zoneType" : "REGULAR_CUSTOM_ZONE"
}
}, {
"recordName" : "75BD67DD-4735-43A1-82E7-0FA3CA9FD354",
"recordType" : "CPLAlbum",
"fields" : {
  "recordModificationDate" : {
    "value" : 1526421228316,
    "type" : "TIMESTAMP"
  },
  "sortAscending" : {
    "value" : 1,
    "type" : "INT64"
  },
  "sortType" : {
    "value" : 0,
    "type" : "INT64"
  },
  "albumType" : {
    "value" : 0,
    "type" : "INT64"
  },
  "albumNameEnc" : {
    "value" : "AzpIAOHJVV0BXCCpOwhGtdNiBYJg/+rJ8+w4/rGVbMY=",
    "type" : "ENCRYPTED_BYTES"
  },
  "position" : {
    "value" : 19456,
    "type" : "INT64"
  },
  "sortTypeExt" : {
    "value" : 0,
    "type" : "INT64"
  }
},
"pluginFields" : { },
"recordChangeTag" : "17y3",
"created" : {
  "timestamp" : 1504919085418,
  "userRecordName" : "_fe283de2507f799cd3a1de7c00348a05",
  "deviceID" : "D4BD5D8AAF15852E345740FF561576ED1B07BA131936558D1391C95E20126C51"
},
"modified" : {
  "timestamp" : 1526422728823,
  "userRecordName" : "_fe283de2507f799cd3a1de7c00348a05",
  "deviceID" : "4A7BAE474C4517CDD3007A007A5C056EB46893C4E56390F57210130EF2E273EA"
},
"deleted" : false,
"zoneID" : {
  "zoneName" : "PrimarySync",
  "ownerRecordName" : "_fe283de2507f799cd3a1de7c00348a05",
  "zoneType" : "REGULAR_CUSTOM_ZONE"
}
}, {
"recordName" : "483C4899-201D-4B6C-BD7C-60064745BDC6",
"recordType" : "CPLAlbum",
"fields" : {
  "recordModificationDate" : {
    "value" : 1588398869642,
    "type" : "TIMESTAMP"
  },
  "sortAscending" : {
    "value" : 1,
    "type" : "INT64"
  },
  "sortType" : {
    "value" : 0,
    "type" : "INT64"
  },
  "albumType" : {
    "value" : 0,
    "type" : "INT64"
  },
  "albumNameEnc" : {
    "value" : "Aw4aAqL7FlDHmhYWV7i2jaRqCq9zouZpEiI7udQG510LGMypBKo=",
    "type" : "ENCRYPTED_BYTES"
  },
  "position" : {
    "value" : 23041,
    "type" : "INT64"
  },
  "sortTypeExt" : {
    "value" : 0,
    "type" : "INT64"
  }
},
"pluginFields" : { },
"recordChangeTag" : "3dmg",
"created" : {
  "timestamp" : 1504919085420,
  "userRecordName" : "_fe283de2507f799cd3a1de7c00348a05",
  "deviceID" : "D4BD5D8AAF15852E345740FF561576ED1B07BA131936558D1391C95E20126C51"
},
"modified" : {
  "timestamp" : 1588398875266,
  "userRecordName" : "_fe283de2507f799cd3a1de7c00348a05",
  "deviceID" : "F39FCC192B5883ED4748C2C05AFA509104E1013F561EC3DE429FF77F51C369CD"
},
"deleted" : false,
"zoneID" : {
  "zoneName" : "PrimarySync",
  "ownerRecordName" : "_fe283de2507f799cd3a1de7c00348a05",
  "zoneType" : "REGULAR_CUSTOM_ZONE"
}
}, {
"recordName" : "0F4419D2-DC24-4925-8ADD-B814FF383769",
"recordType" : "CPLAlbum",
"fields" : {
  "recordModificationDate" : {
    "value" : 1504919079526,
    "type" : "TIMESTAMP"
  },
  "sortAscending" : {
    "value" : 1,
    "type" : "INT64"
  },
  "sortType" : {
    "value" : 0,
    "type" : "INT64"
  },
  "albumType" : {
    "value" : 0,
    "type" : "INT64"
  },
  "albumNameEnc" : {
    "value" : "A5F+AIGBzVQPyPFY/nlxKwvkmbQUz/+vhaMm8VrCDGeg",
    "type" : "ENCRYPTED_BYTES"
  },
  "position" : {
    "value" : 23552,
    "type" : "INT64"
  },
  "sortTypeExt" : {
    "value" : 0,
    "type" : "INT64"
  }
},
"pluginFields" : { },
"recordChangeTag" : "a",
"created" : {
  "timestamp" : 1504919085419,
  "userRecordName" : "_fe283de2507f799cd3a1de7c00348a05",
  "deviceID" : "D4BD5D8AAF15852E345740FF561576ED1B07BA131936558D1391C95E20126C51"
},
"modified" : {
  "timestamp" : 1504919085419,
  "userRecordName" : "_fe283de2507f799cd3a1de7c00348a05",
  "deviceID" : "D4BD5D8AAF15852E345740FF561576ED1B07BA131936558D1391C95E20126C51"
},
"deleted" : false,
"zoneID" : {
  "zoneName" : "PrimarySync",
  "ownerRecordName" : "_fe283de2507f799cd3a1de7c00348a05",
  "zoneType" : "REGULAR_CUSTOM_ZONE"
}
}, {
"recordName" : "54F7AD60-3B35-42F5-9C4F-457B4D28AB50",
"recordType" : "CPLAlbum",
"fields" : {
  "recordModificationDate" : {
    "value" : 1519053959332,
    "type" : "TIMESTAMP"
  },
  "sortAscending" : {
    "value" : 1,
    "type" : "INT64"
  },
  "sortType" : {
    "value" : 0,
    "type" : "INT64"
  },
  "albumType" : {
    "value" : 0,
    "type" : "INT64"
  },
  "albumNameEnc" : {
    "value" : "A0J6AJV3fZntXvnLSIsK2iuunmg+4fIyZyv2EJgMXPI=",
    "type" : "ENCRYPTED_BYTES"
  },
  "position" : {
    "value" : 25089,
    "type" : "INT64"
  },
  "sortTypeExt" : {
    "value" : 0,
    "type" : "INT64"
  }
},
"pluginFields" : { },
"recordChangeTag" : "mys",
"created" : {
  "timestamp" : 1504919085417,
  "userRecordName" : "_fe283de2507f799cd3a1de7c00348a05",
  "deviceID" : "D4BD5D8AAF15852E345740FF561576ED1B07BA131936558D1391C95E20126C51"
},
"modified" : {
  "timestamp" : 1519053960964,
  "userRecordName" : "_fe283de2507f799cd3a1de7c00348a05",
  "deviceID" : "4A7BAE474C4517CDD3007A007A5C056EB46893C4E56390F57210130EF2E273EA"
},
"deleted" : false,
"zoneID" : {
  "zoneName" : "PrimarySync",
  "ownerRecordName" : "_fe283de2507f799cd3a1de7c00348a05",
  "zoneType" : "REGULAR_CUSTOM_ZONE"
}
}, {
"recordName" : "785DFBE0-D93A-4852-BBA0-9CE8B1675898",
"recordType" : "CPLAlbum",
"fields" : {
  "recordModificationDate" : {
    "value" : 1550183144116,
    "type" : "TIMESTAMP"
  },
  "sortAscending" : {
    "value" : 1,
    "type" : "INT64"
  },
  "sortType" : {
    "value" : 0,
    "type" : "INT64"
  },
  "albumType" : {
    "value" : 0,
    "type" : "INT64"
  },
  "albumNameEnc" : {
    "value" : "A14tAm+gCVMi6281yTudHMrCLZvnPhy68lmXBgTfzPQ7S4w=",
    "type" : "ENCRYPTED_BYTES"
  },
  "position" : {
    "value" : 28161,
    "type" : "INT64"
  },
  "sortTypeExt" : {
    "value" : 0,
    "type" : "INT64"
  }
},
"pluginFields" : { },
"recordChangeTag" : "2nh5",
"created" : {
  "timestamp" : 1550183156683,
  "userRecordName" : "_fe283de2507f799cd3a1de7c00348a05",
  "deviceID" : "4A7BAE474C4517CDD3007A007A5C056EB46893C4E56390F57210130EF2E273EA"
},
"modified" : {
  "timestamp" : 1550183156683,
  "userRecordName" : "_fe283de2507f799cd3a1de7c00348a05",
  "deviceID" : "4A7BAE474C4517CDD3007A007A5C056EB46893C4E56390F57210130EF2E273EA"
},
"deleted" : false,
"zoneID" : {
  "zoneName" : "PrimarySync",
  "ownerRecordName" : "_fe283de2507f799cd3a1de7c00348a05",
  "zoneType" : "REGULAR_CUSTOM_ZONE"
}
}, {
"recordName" : "DC206DE5-767D-4A1F-88E7-7BD2A2378EE5",
"recordType" : "CPLAlbum",
"fields" : {
  "recordModificationDate" : {
    "value" : 1604505564315,
    "type" : "TIMESTAMP"
  },
  "sortAscending" : {
    "value" : 1,
    "type" : "INT64"
  },
  "sortType" : {
    "value" : 0,
    "type" : "INT64"
  },
  "albumType" : {
    "value" : 0,
    "type" : "INT64"
  },
  "albumNameEnc" : {
    "value" : "A+OUAuoSpfaHb0ppGADYHTqL8UCdLWEZKah2QFNvBTtlXzL1lLs=",
    "type" : "ENCRYPTED_BYTES"
  },
  "position" : {
    "value" : 1058816,
    "type" : "INT64"
  },
  "sortTypeExt" : {
    "value" : 0,
    "type" : "INT64"
  }
},
"pluginFields" : { },
"recordChangeTag" : "4tsd",
"created" : {
  "timestamp" : 1588397839020,
  "userRecordName" : "_fe283de2507f799cd3a1de7c00348a05",
  "deviceID" : "F39FCC192B5883ED4748C2C05AFA509104E1013F561EC3DE429FF77F51C369CD"
},
"modified" : {
  "timestamp" : 1604505567676,
  "userRecordName" : "_fe283de2507f799cd3a1de7c00348a05",
  "deviceID" : "F39FCC192B5883ED4748C2C05AFA509104E1013F561EC3DE429FF77F51C369CD"
},
"deleted" : false,
"zoneID" : {
  "zoneName" : "PrimarySync",
  "ownerRecordName" : "_fe283de2507f799cd3a1de7c00348a05",
  "zoneType" : "REGULAR_CUSTOM_ZONE"
}
}, {
"recordName" : "08E0FB1B-8D34-4BD5-ACB7-EF926722397B",
"recordType" : "CPLAlbum",
"fields" : {
  "recordModificationDate" : {
    "value" : 1604791546941,
    "type" : "TIMESTAMP"
  },
  "sortAscending" : {
    "value" : 1,
    "type" : "INT64"
  },
  "sortType" : {
    "value" : 0,
    "type" : "INT64"
  },
  "albumType" : {
    "value" : 0,
    "type" : "INT64"
  },
  "albumNameEnc" : {
    "value" : "AzvZApNEU/WEsfD9QDqaAUPbNkoArdlQ86vr25E9+hxxnUdFRuL5",
    "type" : "ENCRYPTED_BYTES"
  },
  "position" : {
    "value" : 1060864,
    "type" : "INT64"
  },
  "sortTypeExt" : {
    "value" : 0,
    "type" : "INT64"
  }
},
"pluginFields" : { },
"recordChangeTag" : "4vhp",
"created" : {
  "timestamp" : 1588397839020,
  "userRecordName" : "_fe283de2507f799cd3a1de7c00348a05",
  "deviceID" : "F39FCC192B5883ED4748C2C05AFA509104E1013F561EC3DE429FF77F51C369CD"
},
"modified" : {
  "timestamp" : 1604991929681,
  "userRecordName" : "_fe283de2507f799cd3a1de7c00348a05",
  "deviceID" : "F39FCC192B5883ED4748C2C05AFA509104E1013F561EC3DE429FF77F51C369CD"
},
"deleted" : false,
"zoneID" : {
  "zoneName" : "PrimarySync",
  "ownerRecordName" : "_fe283de2507f799cd3a1de7c00348a05",
  "zoneType" : "REGULAR_CUSTOM_ZONE"
}
}, {
"recordName" : "825C4576-7DD2-4D03-89FD-CA3E9C958EC3",
"recordType" : "CPLAlbum",
"fields" : {
  "recordModificationDate" : {
    "value" : 1604806072112,
    "type" : "TIMESTAMP"
  },
  "sortAscending" : {
    "value" : 1,
    "type" : "INT64"
  },
  "sortType" : {
    "value" : 0,
    "type" : "INT64"
  },
  "albumType" : {
    "value" : 0,
    "type" : "INT64"
  },
  "albumNameEnc" : {
    "value" : "Az1GAgZVk+MtFupGiwMhZbJ/HcOEPNM3QOnvJpQz5t506lWt83Xg",
    "type" : "ENCRYPTED_BYTES"
  },
  "position" : {
    "value" : 1061888,
    "type" : "INT64"
  },
  "sortTypeExt" : {
    "value" : 0,
    "type" : "INT64"
  }
},
"pluginFields" : { },
"recordChangeTag" : "4vho",
"created" : {
  "timestamp" : 1540509106137,
  "userRecordName" : "_fe283de2507f799cd3a1de7c00348a05",
  "deviceID" : "4A7BAE474C4517CDD3007A007A5C056EB46893C4E56390F57210130EF2E273EA"
},
"modified" : {
  "timestamp" : 1604991929681,
  "userRecordName" : "_fe283de2507f799cd3a1de7c00348a05",
  "deviceID" : "F39FCC192B5883ED4748C2C05AFA509104E1013F561EC3DE429FF77F51C369CD"
},
"deleted" : false,
"zoneID" : {
  "zoneName" : "PrimarySync",
  "ownerRecordName" : "_fe283de2507f799cd3a1de7c00348a05",
  "zoneType" : "REGULAR_CUSTOM_ZONE"
}
}, {
"recordName" : "B75D339C-6D1A-422D-81B5-08AB42ECF765",
"recordType" : "CPLAlbum",
"fields" : {
  "recordModificationDate" : {
    "value" : 1604806755173,
    "type" : "TIMESTAMP"
  },
  "sortAscending" : {
    "value" : 1,
    "type" : "INT64"
  },
  "sortType" : {
    "value" : 0,
    "type" : "INT64"
  },
  "albumType" : {
    "value" : 0,
    "type" : "INT64"
  },
  "albumNameEnc" : {
    "value" : "A1e8AnG0AoF5ExOVH1ROLMwB8yLLujNVnfj+Wv1ExnUMbek1wW0=",
    "type" : "ENCRYPTED_BYTES"
  },
  "position" : {
    "value" : 1062912,
    "type" : "INT64"
  },
  "sortTypeExt" : {
    "value" : 0,
    "type" : "INT64"
  }
},
"pluginFields" : { },
"recordChangeTag" : "4vhn",
"created" : {
  "timestamp" : 1549262248639,
  "userRecordName" : "_fe283de2507f799cd3a1de7c00348a05",
  "deviceID" : "4A7BAE474C4517CDD3007A007A5C056EB46893C4E56390F57210130EF2E273EA"
},
"modified" : {
  "timestamp" : 1604991929681,
  "userRecordName" : "_fe283de2507f799cd3a1de7c00348a05",
  "deviceID" : "F39FCC192B5883ED4748C2C05AFA509104E1013F561EC3DE429FF77F51C369CD"
},
"deleted" : false,
"zoneID" : {
  "zoneName" : "PrimarySync",
  "ownerRecordName" : "_fe283de2507f799cd3a1de7c00348a05",
  "zoneType" : "REGULAR_CUSTOM_ZONE"
}
}, {
"recordName" : "7EAC2C7A-D78B-448D-8766-ECF4A6E0B91D",
"recordType" : "CPLAlbum",
"fields" : {
  "recordModificationDate" : {
    "value" : 1606475312891,
    "type" : "TIMESTAMP"
  },
  "sortAscending" : {
    "value" : 1,
    "type" : "INT64"
  },
  "sortType" : {
    "value" : 0,
    "type" : "INT64"
  },
  "albumType" : {
    "value" : 0,
    "type" : "INT64"
  },
  "albumNameEnc" : {
    "value" : "A3RXAqWK0dSo1OBXGhNo27JSh6gCOmdSl5L9XvPAqeFxrWP0vjFgPjMQ0w==",
    "type" : "ENCRYPTED_BYTES"
  },
  "position" : {
    "value" : 1064960,
    "type" : "INT64"
  },
  "sortTypeExt" : {
    "value" : 0,
    "type" : "INT64"
  }
},
"pluginFields" : { },
"recordChangeTag" : "4ytj",
"created" : {
  "timestamp" : 1603778689510,
  "userRecordName" : "_fe283de2507f799cd3a1de7c00348a05",
  "deviceID" : "F39FCC192B5883ED4748C2C05AFA509104E1013F561EC3DE429FF77F51C369CD"
},
"modified" : {
  "timestamp" : 1606475313699,
  "userRecordName" : "_fe283de2507f799cd3a1de7c00348a05",
  "deviceID" : "F39FCC192B5883ED4748C2C05AFA509104E1013F561EC3DE429FF77F51C369CD"
},
"deleted" : false,
"zoneID" : {
  "zoneName" : "PrimarySync",
  "ownerRecordName" : "_fe283de2507f799cd3a1de7c00348a05",
  "zoneType" : "REGULAR_CUSTOM_ZONE"
}
} ],
"syncToken" : "AQAAAAAABS7mf/////////83PBlVe6dLKYUBtUbwLolK"
}
*/

public class QueryRequest
{
    public QueryRequest(string recordType)
    {
        Query = new(recordType);
    }

    public QueryRequest()
    {
    }

    public RecordRequest Query { get; set; }
    public ZoneID ZoneID { get; set; } = ZoneID.Default;
    public int? ResultsLimit { get; set; }
    public string[] DesiredKeys { get; set; }
}

public class QueryResponse
{
    public Record[] Records { get; init; }
    public string SyncToken { get; init; }
}

public record RecordRequest(string RecordType)
{
    public QueryFilter[] FilterBy { get; init; }
}

public record Record(string RecordType)
{
    public string RecordName { get; init; }
    public IReadOnlyDictionary<string, Field> Fields { get; init; }
    public IReadOnlyDictionary<string, Field> PluginFields { get; init; }
    public string RecordChangeTag { get; init; }
    public ActionDate Created { get; init; }
    public ActionDate Modified { get; init; }
    public bool Deleted { get; init; }
    public ZoneID ZoneID { get; init; }
}

public record Field(string Type, object Value)
{
    public object GetConvertedValue()
    {
        if (Value is not JsonElement element)
        {
            return Value;
        }

        switch (Type)
        {
            case "INT64":
                return element.GetInt64();
            case "STRING":
                return element.GetString();
            case "TIMESTAMP":
                return DateTimeOffset.FromUnixTimeMilliseconds(element.GetInt64()).UtcDateTime;
            case "ENCRYPTED_BYTES":
                return Encoding.UTF8.GetString(
                    Convert.FromBase64String(element.GetString()));
            case "ASSETID":
                return element.Deserialize<AssetId>(JsonSerializeOptions.CamelCaseProperties);
            case "REFERENCE":
                return element.Deserialize<RecordReference>(JsonSerializeOptions.CamelCaseProperties);

            default:
                return Value;
        }
    }
}

public record RecordReference(string RecordName, string Action, ZoneID ZoneID);


public class AssetId
{
    public string FileChecksum { get; init; }
    public long Size { get; init; }
    public string WrappingKey { get; init; }
    public string ReferenceChecksum { get; init; }
    public string DownloadURL { get; init; }
}

/*
internal class FieldConverter : JsonConverter<Field>
{
    public override Field Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var field = JsonSerializer.Deserialize<Field>(ref reader, options);
        switch (field.Type)
        {
            case "INT64":
                field.Value = Convert.ToInt64(field.Value);
                break;

            case "RECORDREFERENCE":
                field.Value = JsonSerializer.Deserialize
        }

        JsonSerializer.Deserialize<Record>
{

        }
    }

    public override void Write(Utf8JsonWriter writer, Field value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, options);
    }
}*/

public class ActionDate
{
    public long Timestamp { get; init; }
    public string UserRecordName { get; init; }
    public string DeviceID { get; init; }
}

public record ZoneID(string ZoneName)
{
    public static readonly ZoneID Default = new("PrimarySync")
    {
        //OwnerRecordName = "_fe283de2507f799cd3a1de7c00348a05",
        ZoneType = "REGULAR_CUSTOM_ZONE"
    };


    public string OwnerRecordName { get; init; }
    public string ZoneType { get; init; }
}
