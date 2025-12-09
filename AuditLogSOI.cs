// Copyright 2018 ESRI
// 
// All rights reserved under the copyright laws of the United States
// and applicable international laws, treaties, and conventions.
// 
// You may freely redistribute and use this sample code, with or
// without modification, provided you include the original copyright
// notice and use restrictions.
// 
// See the use restrictions at <your Enterprise SDK install location>/userestrictions.txt.
// 

using ArcGisPbf;
using ESRI.ArcGIS.Carto;
using ESRI.ArcGIS.esriSystem;
using ESRI.ArcGIS.Geodatabase;
using ESRI.ArcGIS.Geometry;
using ESRI.ArcGIS.Server;
using ESRI.Server.SOESupport;
using ESRI.Server.SOESupport.SOI;
using EsriPBuffer;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
//using System.Text.Json;
//using System.Text.Json.Nodes;


//This is REST SOE template of Enterprise SDK

//TODO: sign the project (project properties > signing tab > sign the assembly)
//      this is strongly suggested if the dll will be registered using regasm.exe <your>.dll /codebase


namespace AuditLogSOI
{
    [ComVisible(true)]
    [Guid("e61db380-0c77-4138-8064-50b0a656b902")]
    [ClassInterface(ClassInterfaceType.None)]
    [ServerObjectInterceptor("MapServer",
        Description = "",
        DisplayName = "AuditLogSOI",
        Properties = "",
        SupportsSharedInstances = false)]
    public class AuditLogSOI : IServerObjectExtension, IRESTRequestHandler, IWebRequestHandler, IRequestHandler2, IRequestHandler
    {
        private string _soiName;
        private IServerObjectHelper _soHelper;
        private ServerLogger _serverLog;
        private RestSOIHelper _restSOIHelper;

        public AuditLogSOI()
        {
            _soiName = this.GetType().Name;
        }

        public void Init(IServerObjectHelper pSOH)
        {
            _soHelper = pSOH;
            _serverLog = new ServerLogger();
            _restSOIHelper = new RestSOIHelper(pSOH);
            _serverLog.LogMessage(ServerLogger.msgType.infoStandard, _soiName + ".init()", 200, "Initialized " + _soiName + " SOI.");
        }

        public void Shutdown()
        {
            _serverLog.LogMessage(ServerLogger.msgType.infoStandard, _soiName + ".init()", 200, "Shutting down " + _soiName + " SOI.");
        }

        #region REST interceptors

        public string GetSchema()
        {
            IRESTRequestHandler restRequestHandler = _restSOIHelper.FindRequestHandlerDelegate<IRESTRequestHandler>();
            if (restRequestHandler == null)
                return null;

            return restRequestHandler.GetSchema();
        }

        public byte[] HandleRESTRequest(string Capabilities, string resourceName, string operationName,
            string operationInput, string outputFormat, string requestProperties, out string responseProperties)
        {
            responseProperties = null;
            // _serverLog.LogMessage(ServerLogger.msgType.infoStandard, _soiName + ".HandleRESTRequest()", 200, "Request received in Sample Object Interceptor for handleRESTRequest");

            IRESTRequestHandler restRequestHandler = _restSOIHelper.FindRequestHandlerDelegate<IRESTRequestHandler>();
            if (restRequestHandler == null)
                return null;

            /*
            * Add code to manipulate REST requests here
            */
            if (operationName == "query")
            {
                var loginUser = "current user=" + SOIBase.GetServerEnvironment().UserInfo.Name;
                string userAction = String.Format("SOI Intercepted Query. Capabilities={0}, resourceName={1}, operationName={2}, operationInput={3}, outputFormat={4}, " +
                    "requestProperties={5}, loginUser={6}", Capabilities, resourceName, operationName, operationInput, outputFormat, requestProperties, loginUser);

                // _serverLog.LogMessage(ServerLogger.msgType.infoSimple, "HandleRESTRequest()", 200, userAction);

                string resultJSONStr = null;
                List<string> globalIds = new List<string>();

                byte[] originalResponse = restRequestHandler.HandleRESTRequest(Capabilities, resourceName, operationName, operationInput, outputFormat, requestProperties, out responseProperties);

                if (outputFormat == "pbf")
                {
                    var decodedPBF = new FeatureCollectionDecoder().Decode(originalResponse); //FeatureCollectionPBuffer.Parser.ParseFrom(originalResponse);
                    decodedPBF.FeatureCollection.Features.ToList().ForEach(feature =>
                    {
                        if (feature.Properties != null && feature.Properties.TryGetValue("GlobalID", out var globalIdValue) && globalIdValue != null)
                        {
                            globalIds.Add(globalIdValue.ToString());
                        }
                    });
                }
                else if (outputFormat == "json")
                {
                    resultJSONStr = System.Text.Encoding.UTF8.GetString(originalResponse);

                    if (!String.IsNullOrEmpty(resultJSONStr))
                    {
                        ESRI.Server.SOESupport.JsonObject resultJSON = new ESRI.Server.SOESupport.JsonObject(resultJSONStr);
                        object[] features;
                        if (resultJSON != null && resultJSON.TryGetArray("features", out features))
                        {
                            foreach (var feature in features)
                            {
                                JsonObject jsonFeature = feature as JsonObject;
                                JsonObject attributes;
                                if (jsonFeature != null && jsonFeature.TryGetJsonObject("attributes", out attributes))
                                {
                                    if (attributes != null && attributes.TryGetString("GlobalID", out var globalIdValue))
                                    {
                                        globalIds.Add(globalIdValue);
                                    }
                                }
                            }
                        }
                    }
                }
                // Log the username and the GlobalIDs of the features returned
                if(globalIds.Count > 0) { 
                    _serverLog.LogMessage(ServerLogger.msgType.infoSimple, "HandleRESTRequest()", 200,
                        String.Format("User: {0} | Resource: {1} | GlobalIDs: {2}", SOIBase.GetServerEnvironment().UserInfo.Name, resourceName,
                        String.Join(", ", globalIds)));
                }
            }
            else
            {
                string userAction = String.Format("SOI Intercepted {2}. Capabilities={0}, resourceName={1}, operationInput={3}, outputFormat={4}, " +
                    "requestProperties={5}", Capabilities, resourceName, operationName, operationInput, outputFormat, requestProperties);

                _serverLog.LogMessage(ServerLogger.msgType.infoSimple, "HandleRESTRequest()", 200, userAction);
            }

            return restRequestHandler.HandleRESTRequest(
                    Capabilities, resourceName, operationName, operationInput,
                    outputFormat, requestProperties, out responseProperties);
        }

        #endregion

        #region SOAP interceptors

        public byte[] HandleStringWebRequest(esriHttpMethod httpMethod, string requestURL,
            string queryString, string Capabilities, string requestData,
            out string responseContentType, out esriWebResponseDataType respDataType)
        {
            _serverLog.LogMessage(ServerLogger.msgType.infoStandard, _soiName + ".HandleStringWebRequest()",
                200, "Request received in Sample Object Interceptor for HandleStringWebRequest");

            /*
             * Add code to manipulate requests here
             */

                    IWebRequestHandler webRequestHandler = _restSOIHelper.FindRequestHandlerDelegate<IWebRequestHandler>();
            if (webRequestHandler != null)
            {
                return webRequestHandler.HandleStringWebRequest(
                        httpMethod, requestURL, queryString, Capabilities, requestData, out responseContentType, out respDataType);
            }

            responseContentType = null;
            respDataType = esriWebResponseDataType.esriWRDTPayload;
            //Insert error response here.
            return null;
        }

        public byte[] HandleBinaryRequest(ref byte[] request)
        {
            _serverLog.LogMessage(ServerLogger.msgType.infoStandard, _soiName + ".HandleBinaryRequest()",
                  200, "Request received in Sample Object Interceptor for HandleBinaryRequest");

            /*
             * Add code to manipulate requests here
             */

            IRequestHandler requestHandler = _restSOIHelper.FindRequestHandlerDelegate<IRequestHandler>();
            if (requestHandler != null)
            {
                return requestHandler.HandleBinaryRequest(request);
            }

            //Insert error response here.
            return null;
        }

        public byte[] HandleBinaryRequest2(string Capabilities, ref byte[] request)
        {
            _serverLog.LogMessage(ServerLogger.msgType.infoStandard, _soiName + ".HandleBinaryRequest2()",
                  200, "Request received in Sample Object Interceptor for HandleBinaryRequest2");

            /*
             * Add code to manipulate requests here
             */

            IRequestHandler2 requestHandler = _restSOIHelper.FindRequestHandlerDelegate<IRequestHandler2>();
            if (requestHandler != null)
            {
                return requestHandler.HandleBinaryRequest2(Capabilities, request);
            }

            //Insert error response here.
            return null;
        }

        public string HandleStringRequest(string Capabilities, string request)
        {
            _serverLog.LogMessage(ServerLogger.msgType.infoStandard, _soiName + ".HandleStringRequest()",
                   200, "Request received in Sample Object Interceptor for HandleStringRequest");

            /*
             * Add code to manipulate requests here
             */

            IRequestHandler requestHandler = _restSOIHelper.FindRequestHandlerDelegate<IRequestHandler>();
            if (requestHandler != null)
            {
                return requestHandler.HandleStringRequest(Capabilities, request);
            }

            //Insert error response here.
            return null;
        }

        #endregion

    }
}
