# What it is
Implementation of an ArcGIS Server Object Interceptor using the [ArcGIS Enterprise SDK for .NET](https://developers.arcgis.com/enterprise-sdk/#install-arcgis-enterprise-sdk). 
SOI logs the username, resource (layer ids), and configurable attribute of features returned from the query to a Map (or Feature) Service's REST endpoint.

# How to build it
### Prerequisites: 
* Visual Studio (not sure what edition / version - the solution and project were built using Visual Studio Professional 2022)
* Refer to [Esri's docs to install the ArcGIS Enterprise SDK for .Net](https://developers.arcgis.com/enterprise-sdk/guide/net/installation-net/)  (Make sure you install the correct version for your instance of ArcGIS Enterprise).
  
1. Download this repository
2. Open AuditLogSOI.sln in Visual Studio
3. In the Solution Explorer window, right click on the Solution and select "Rebuild Solution".  This will create the .soe file that you will need to deploy to ArcGIS Server. The .soe file will be located in: <Solution Root>\bin\[Debug | Release]\net8.0\win-x64 

# How to deploy it
### Prerequisite: [Enable .NET Extension Support on ArcGIS Enterprise](https://developers.arcgis.com/enterprise-sdk/guide/net/deploy-extensions-net/)
1. Log in to your ArcGIS Enterprise's ArcGIS Server Manager
2. Select Site -> Extensions
3. Click Add Extension -> Choose File
4. Select the AuditLogSOI_ent.soe file and click Add
5. In Server Manager, navigate to the service you want to log, and open it's configuration page.
6. Select Capabilities
7. Under Interceptors->Available Interceptors, double-click "AuditLogSOI so it copies to the Enabled Interceptors box.
8. Under AuditLogSOI Configuration -> Properties, enter the name of the attribute you wish to log for each feature included in a query response. The default is OBJECTID. Note: the name of the attribute is case-sensitive.

# How to test it
1. Open the map service in web map viewer and perfom and click on one of its features to open its popup.
2. Back in Server Manager, go to the log viewer, change the Log Filter level to Info
3. Select the map service on which you enabled the SOI and click Query. 
You should see a log state like: User: <your username> | Resource: layers/2 | OBJECTIDs: 3, 5
