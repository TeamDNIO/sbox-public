using System.IO;

namespace Editor;

public record PackageLocation : LocalAssetBrowser.Location
{
	Package ParentPackage { get; init; }
	string WorkshopFolder { get; init; }

	public PackageLocation( Package package ) : base( package.Title, "supervisor_account" )
	{
		ParentPackage = package;
		WorkshopFolder = AssetSystem.GetWorkshopFolder( package );

		if ( WorkshopFolder is not null )
		{
			Path = WorkshopFolder;
			RootPath = WorkshopFolder;
			RootTitle = package.Title;
			RelativePath = "";
			IsRoot = true;
			Type = LocalAssetBrowser.LocationType.Assets;
		}
	}

	PackageLocation( Package package, string rootFolder, string folder ) : base( System.IO.Path.GetFileName( folder ), "folder" )
	{
		ParentPackage = package;
		WorkshopFolder = folder;
		Path = folder;
		RootPath = rootFolder;
		RootTitle = package.Title;
		RelativePath = System.IO.Path.GetRelativePath( rootFolder, folder );
		Type = LocalAssetBrowser.LocationType.Assets;
	}

	public override bool CanGoUp() => WorkshopFolder is not null && !IsRoot;

	public override List<LocalAssetBrowser.Location> GetDirectories()
	{
		if ( WorkshopFolder is null || !Directory.Exists( WorkshopFolder ) )
			return new List<LocalAssetBrowser.Location>();

		return Directory.GetDirectories( WorkshopFolder )
			.Where( d => !System.IO.Path.GetFileName( d ).StartsWith( '.' ) )
			.OrderBy( d => d, StringComparer.OrdinalIgnoreCase )
			.Select( d => (LocalAssetBrowser.Location)new PackageLocation( ParentPackage, RootPath, d ) )
			.ToList();
	}

	public override IEnumerable<FileInfo> GetFiles()
	{
		if ( WorkshopFolder is null )
			return AssetSystem.GetPackageFiles( ParentPackage ).Select( x => new FileInfo( FileSystem.Cloud.GetFullPath( x ) ) );

		if ( !Directory.Exists( WorkshopFolder ) )
			return Enumerable.Empty<FileInfo>();

		return Directory.GetFiles( WorkshopFolder )
			.Select( f => new FileInfo( f ) )
			.Where( f => !f.Name.StartsWith( '.' ) );
	}
}
