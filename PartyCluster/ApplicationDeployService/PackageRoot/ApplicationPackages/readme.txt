Add application packages as ZIP files.
ZIP root should be the application package root; contents of the ZIP should NOT be in another directory.

For each application package, add an entry in ApplicationPackages.json config package:

	{
        "applicationTypeName": "applicationtype",
        "applicationTypeVersion": "1.0.0",
        "packageFileName": "myapppackage.zip",
        "entryServiceInstanceUri":  "fabric:/application/service",
        "entryServiceEndpointName":  "",
		"serviceInfoUrl": "",
        "applicationDescription": "This is a fabulous app!"
    }