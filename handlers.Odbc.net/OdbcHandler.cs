using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Odbc;
using System.IO;
using System.Text.RegularExpressions;

using Newtonsoft.Json;

using Synapse.Core;


public class OdbcHandler : HandlerRuntimeBase
{
    ConnectionInfo _dsn = null;

    public override IHandlerRuntime Initialize(string config)
    {
        //deserialize the Config from the Handler declaration
        _dsn = DeserializeOrNew<ConnectionInfo>( config );
        //update for any dynamic values
        _dsn.ConnectionString = ProcessExpressions( _dsn.ConnectionString, _dsn.Expressions );
        return this;
    }

    public override ExecuteResult Execute(HandlerStartInfo startInfo)
    {
        //declare/initialize method-scope variables
        int cheapSequence = 0; //used to order message flowing out from the Handler
        const string __context = "Execute";
        ExecuteResult result = new ExecuteResult()
        {
            Status = StatusType.Complete,
            Sequence = Int32.MaxValue
        };
        string msg = "Complete";
        Exception exc = null;

        //deserialize the Parameters from the Action declaration
        OdbcHandlerParameters parms = DeserializeOrNew<OdbcHandlerParameters>( startInfo.Parameters );
        //update for any dynamic values
        parms.QueryString = ProcessExpressions( parms.QueryString, parms.Expressions );

        using( OdbcConnection connection = new OdbcConnection( _dsn.ConnectionString ) )
        {
            try
            {
                //if IsDryRun == true, test if ConnectionString is valid and works.
                if( startInfo.IsDryRun )
                {
                    OnProgress( __context, "Attempting connection", sequence: cheapSequence++ );
                    connection.Open();

                    result.ExitData = connection.State;
                    result.Message = msg =
                        $"Connection test successful! Connection string: {_dsn.ConnectionString}";
                }
                //else, select data as declared in Parameters.QueryString
                else
                {
                    //stores the data, once selected
                    DataSet dataSet = new DataSet();

                    OdbcDataAdapter adapter = new OdbcDataAdapter( parms.QueryString, connection );

                    //get the data
                    OnProgress( __context, $"Executing query: {parms.QueryString}", sequence: cheapSequence++ );
                    connection.Open();
                    adapter.Fill( dataSet );

                    //serialize the data per requested format (Json/Xml)
                    OnProgress( __context, "Serializing result", sequence: cheapSequence++ );
                    string data = null;
                    if( parms.ReturnFormat == SerializationFormat.Json )
                        data = JsonConvert.SerializeObject( dataSet, Formatting.None );
                    else
                        using( StringWriter writer = new StringWriter() )
                        {
                            dataSet.Tables[0].WriteXml( writer );
                            data = writer.ToString();
                        }

                    //populate the Handler result
                    result.ExitData = data;
                }
            }
            //something wnet wrong: hand-back the Exception and mark the execution as Failed
            catch( Exception ex )
            {
                exc = ex;
                result.Status = StatusType.Failed;
                result.ExitData = msg =
                    ex.Message;
            }
            finally
            {
                connection.Close();
            }

            //final runtime notification, return sequence=Int32.MaxValue by convention to supercede any other status message
            OnProgress( __context, msg, result.Status, sequence: Int32.MaxValue, ex: exc );

            return result;
        }
    }

    string ProcessExpressions(string input, List<ExpressionItem> expressions)
    {
        foreach( ExpressionItem expr in expressions )
            input = Regex.Replace( input, expr.Find, expr.ReplaceWith, RegexOptions.IgnoreCase );

        return input;
    }
}

public class ConnectionInfo
{
    public string ConnectionString { get; set; }
    public List<ExpressionItem> Expressions { get; set; }
}

public enum SerializationFormat
{
    Json,
    Xml
}

public class OdbcHandlerParameters
{
    public string QueryString { get; set; }
    public List<ExpressionItem> Expressions { get; set; } = new List<ExpressionItem>();
    public SerializationFormat ReturnFormat { get; set; }
}

public class ExpressionItem
{
    public string Find { get; set; }
    public string ReplaceWith { get; set; }
    public string Encoding { get; set; }
}