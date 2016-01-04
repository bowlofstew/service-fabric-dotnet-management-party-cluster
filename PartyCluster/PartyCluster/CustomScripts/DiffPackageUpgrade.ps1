# Get the application path. This assumes that this script is in a directory inside the application project directory.
# For example: SolutionDir/ApplicationProj/CustomScripts/DiffPackageUpgrade.ps1
$dp0 = Split-Path -parent $PSCommandPath
$applicationDir = (Get-Item "$dp0\..\").FullName
$applicationPackageDir = "$applicationDir\pkg\Release"

# The current application version. This needs to match the application type version currently deployed in the cluster.
$version = "1.0.7"

# The application version that we're upgrading to. This needs to match the application type version in ApplicationManifest.xml.
$newVersion = "1.0.8"

# Connect to the cluster. This cluster is secured, so we provide a certificate thumbprint for the certificate that is used to authenticate with the cluster. 
# This certificate's private key must be installed on the machine that is connecting to the secure cluster.
Connect-ServiceFabricCluster -ConnectionEndpoint "" -X509Credential  -FindType FindByThumbprint -FindValue "" -ServerCertThumbprint "" -StoreLocation CurrentUser -StoreName My

# Test the application package first.
Test-ServiceFabricApplicationPackage -ApplicationPackagePath $applicationPackageDir -ImageStoreConnectionString fabric:imageStore -ApplicationParameter @{"PackageTempDirectory"="D:\Party\Packages"}

# Copy the application package up to the Service Fabric image store. 
# This uses the application type version in the location path for the package to differentiate from previous versions.
Copy-ServiceFabricApplicationPackage -ApplicationPackagePath $applicationPackageDir -ImageStoreConnectionString fabric:ImageStore -ApplicationPackagePathInImageStore "PartyCluster\$version"

# Register the new version of the application type in the cluster.
# At this point, with the new application type registered, we can issue commands to either upgrade the existing application to this new version,
# but we can also create a new instance of this new version, with an instance of the old version still running, which can be useful for side-by-side testing.
Register-ServiceFabricApplicationType -ApplicationPathInImageStore "PartyCluster\$version"

# Start the upgrade.
# The -ApplicationParameter is a set of parameters that are defined in ApplicationManifest.xml that we can set here.
# When deploying with Visual Studio, these parameters can be set in the ApplicationParameters files (Local.xml, Cloud.xml, etc).
Start-ServiceFabricApplicationUpgrade -ApplicationName fabric:/PartyCluster -ApplicationTypeVersion $newVersion -ApplicationParameter @{"PackageTempDirectory"="D:\Party\Packages"} -FailureAction Rollback -Monitored
