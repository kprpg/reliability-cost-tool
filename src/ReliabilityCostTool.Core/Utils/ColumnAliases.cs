namespace ReliabilityCostTool.Core.Utils;

public static class ColumnAliases
{
    public static readonly string[] ResourceName =
    [
        "resource name",
        "name",
        "resource"
    ];

    public static readonly string[] ResourceType =
    [
        "resource type",
        "type",
        "service",
        "azure service"
    ];

    public static readonly string[] Region =
    [
        "region",
        "location",
        "azure region"
    ];

    public static readonly string[] Sku =
    [
        "sku",
        "sku name",
        "size",
        "tier",
        "vm size",
        "service tier"
    ];

    public static readonly string[] Quantity =
    [
        "instance count",
        "instances",
        "count",
        "quantity",
        "qty",
        "nodes"
    ];

    public static readonly string[] Capacity =
    [
        "capacity",
        "capacity gb",
        "used capacity",
        "size gb",
        "storage gb",
        "allocated storage",
        "provisioned storage",
        "database size",
        "data size"
    ];

    public static readonly string[] ReliabilitySignals =
    [
        "zone redundant",
        "zonal redundancy",
        "zone redundancy",
        "availability zone",
        "availability zones",
        "high availability",
        "ha",
        "backup",
        "site recovery",
        "dr",
        "reliability",
        "redundancy",
        "resilience",
        "findings",
        "recommendation",
        "notes"
    ];

    public static readonly string[] AzureSqlDbSection =
    [
        "azure sql db resiliency",
        "azure sql db resilience"
    ];

    public static readonly string[] AzureSqlDbByTier =
    [
        "azure sql db by tier"
    ];

    public static readonly string[] AzureSqlDbCount =
    [
        "#of dbs",
        "number of dbs",
        "db count"
    ];

    public static readonly string[] AzureSqlZoneRedundantDbCount =
    [
        "#zone redundant azure sql dbs",
        "zone redundant azure sql dbs"
    ];

    public static readonly string[] AzureSqlGeoReplicatedDbCount =
    [
        "#of dbs with geo-rep",
        "#of dbs with geo rep",
        "dbs with geo-rep",
        "dbs with geo rep"
    ];
}
