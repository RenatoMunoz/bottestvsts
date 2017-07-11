﻿using System;
using System.ComponentModel;
using System.Globalization;
using Microsoft.VisualStudio.TestTools.WebTesting;
using Microsoft.VisualStudio.TestTools.LoadTesting;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Remoting.Contexts;
using System.Text;
using System.Threading.Tasks;
using HelperLib;
using JsonExtractionRule;

namespace HelperLib
{
    [DisplayName("JSON Extraction Rule")]
    [Description("Extracts the specified JSON value from an object.")]
    public class JsonExtractionRule : ExtractionRule
    {
        public string Name { get; set; }

        public override void Extract(object sender, ExtractionEventArgs e)
        {
            try
            {
                e.Success = true;

                if (e.Response.BodyString != null)
                {
                    e.WebTest.Context.Add(Constants.Context_BotResponseReceived, "false");

                    var json = e.Response.BodyString;
                    if (!string.IsNullOrEmpty(json))
                    {
                        var data = JObject.Parse(json);

                        if (data != null)
                        {
                            var v = data.SelectToken(Name);
                            e.WebTest.Context.Add(this.ContextParameterName, v);

                            return;
                        }
                    }
                }

                e.Message = String.Format(CultureInfo.CurrentCulture, "Not Found: {0}", Name);
            }
            catch (Exception ex)
            {
                e.Success = false;
                e.WebTest.Context[Constants.Context_ConvId] = e.WebTest.Context.ContainsKey(Constants.Context_ConvId)
                    ? e.WebTest.Context[Constants.Context_ConvId].ToString()
                    : $"ConvIdNotFound-{Guid.NewGuid()}";
                e.WebTest.Context[Constants.Context_TestStatus] = false;
                e.WebTest.Context[Constants.Context_TestStatusMessage] = ex.Message;
                e.Message = ex.Message;
            }
        }
    }

    [DisplayName("Response Message Validation Rule")]
    [Description("Response Message Validation Rule")]
    public class ResponseMessageValidationRule : ValidationRule
    {
        public override void Validate(object sender, ValidationEventArgs e)
        {
            string convId = e.WebTest.Context[Constants.Context_ConvId].ToString();

            string userMessageId = e.WebTest.Context[Constants.Context_MessageId].ToString();
            string ExpectedResult = e.WebTest.Context[Constants.Context_ExpectedResult].ToString();
            string LuisQnA = e.WebTest.Context[Constants.Context_LuisQnA].ToString();
            string BusinessArea = e.WebTest.Context[Constants.Context_BusinessArea].ToString();

            try
            {
                if (e.Response.BodyString != null)
                {
                    var json = e.Response.BodyString;

                    var data = JObject.Parse(json);
                    Dictionary<string, object> dataDictionary = data.ToObject<Dictionary<string, object>>();

                    Dictionary<string, dynamic> activityDetails = new Dictionary<string, dynamic>();
                    if (dataDictionary != null && dataDictionary.ContainsKey("activities"))
                    {
                        List<object> activities = ((JArray) dataDictionary["activities"]).ToList<object>();

                        if (activities.Count > 0)
                        {
                            e.WebTest.Context[Constants.Context_ActivityCount] = activities.Count;

                            foreach (var item in activities)
                            {
                                JObject activityInfo = item as JObject;
                                if (activityInfo != null)
                                {
                                    string messageId = activityInfo["id"].ToString();
                                    string text = activityInfo["text"].ToString();
                                    if (!string.IsNullOrEmpty(text))
                                    {
                                        activityDetails.Add(messageId,
                                            new
                                            {
                                                id = messageId,
                                                fromId = activityInfo["from"]["id"],
                                                fromName = activityInfo["from"]["name"],
                                                text = text,
                                                timestamp = activityInfo["timestamp"],
                                                channel = activityInfo["channelId"],
                                                replyTo = activityInfo["replyToId"] != null?
                                                          activityInfo["replyToId"].ToString() : string.Empty
                                            });
                                    }
                                }
                            }

                            dynamic botReplyActivity =
                                activityDetails.Values.FirstOrDefault(
                                    v => v.replyTo != null && v.replyTo.ToString().Contains(convId));

                            if (botReplyActivity != null)
                            {
                                string botMessageId = botReplyActivity.id;
                                e.WebTest.Context[Constants.Context_BotResponseReceived] = "true";
                                DateTime requestTime = DateTime.Parse(activityDetails[userMessageId].timestamp.ToString());
                                DateTime responseTime = DateTime.Parse(botReplyActivity.timestamp.ToString());
                                double timeTaken = responseTime.Subtract(requestTime).TotalMilliseconds;

                                string actualResult = botReplyActivity.text.ToString();
                                bool testMatch =
                                    ExpectedResult.Equals(actualResult, StringComparison.OrdinalIgnoreCase);
                                string status = "succeeded";
                                e.Message = $"Request [{userMessageId}] and response [{botMessageId}] validation " +
                                            status;
                                e.Message += string.Format(", Actual result {0} Expected result",
                                    testMatch ? "matches" : "doesn't match");


                                //ReportHelper.WriteLog(convId, activityDetails[userMessageId].text.ToString(), ExpectedResult, actualResult, status, testMatch.ToString(), timeTaken, botReplyActivity.channel.ToString(), BusinessArea, LuisQnA);

                                e.WebTest.Context[Constants.Context_ActualResult] = actualResult;
                                e.WebTest.Context[Constants.Context_TestMatch] = testMatch;
                                e.WebTest.Context[Constants.Context_TestStatus] = true;
                                e.WebTest.Context[Constants.Context_TestStatusMessage] = string.Empty;
                                e.WebTest.Context[Constants.Context_Channel] = botReplyActivity.channel.ToString();
                                e.WebTest.Context[Constants.Context_Duration] = timeTaken;
                                return;
                            }
                            else
                            {
                                int retryCount = 0;
                                if (e.WebTest.Context.ContainsKey(Constants.Context_RetryCount))
                                {
                                    retryCount = int.Parse(e.WebTest.Context[Constants.Context_RetryCount].ToString());
                                }

                                e.WebTest.Context[Constants.Context_RetryCount] = ++retryCount;

                                if (retryCount == 10)
                                {
                                    e.IsValid = false;
                                    //ReportHelper.WriteLog(convId, activityDetails[userMessageId].text.ToString(), ExpectedResult, "No response from bot", "Failed", bool.FalseString, 0);
                                    e.WebTest.Context[Constants.Context_TestStatus] = false;
                                    e.WebTest.Context[Constants.Context_BotResponseReceived] = "true";
                                    e.WebTest.Context[Constants.Context_TestStatusMessage] = "No response from bot";
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                e.WebTest.Context[Constants.Context_TestStatus] = false;
                e.Message = ex.Message;
            }
        }
    }

    [DisplayName("Message Sent Validation Rule")]
    [Description("Extracts the specified JSON value from an object.")]
    public class JsonMessageSentValidationRule : ValidationRule
    {
        public override void Validate(object sender, ValidationEventArgs e)
        {
            if (e.Response.StatusCode == HttpStatusCode.OK)
            {
                WebTestContext context = e.WebTest.Context;             

                context[Constants.Context_MessagePostedToBot] = true;
                context[Constants.Context_TestStatus] = true;
                context[Constants.Context_ActualResult] = "Sent";

                if (context.ContainsKey(Constants.Context_MessageId) && context.ContainsKey(Constants.Context_UserActivity))
                {
                    string jsonInput = context[Constants.Context_UserActivity].ToString();
                    Activity activity = JsonConvert.DeserializeObject<Activity>(jsonInput);
                    activity.Timestamp = DateTime.UtcNow.AddSeconds(-2);
                    context[Constants.Context_UserActivity] = JsonConvert.SerializeObject(activity, new JsonSerializerSettings(){ NullValueHandling = NullValueHandling.Ignore});
                }
            }
        }
    }

    [DisplayName("Conditional rule - PrepareActivityData")]
    [Description("Conditional rule to Prepare Activity Data")]
    public class PrepareActivityData : ConditionalRule
    {
        public override void CheckCondition(object sender, ConditionalEventArgs e)
        {
            string dataSourceName = e.WebTest.Context["DataSourceName"].ToString();
            string dataSourceTableName = e.WebTest.Context["DataSourceTableName"].ToString();
            string dsPath = $"{dataSourceName}.{dataSourceTableName}";
            List<string> fields = e.WebTest.Context["DataFieldsToExtract"].ToString().Split(',').ToList();
            foreach (var field in fields)
            {
                //{{Test.TestData#csv.Utterance}}
                string fieldName = $"{dsPath}.{field}";

                e.WebTest.Context[field] = e.WebTest.Context[fieldName].ToString().Replace(';', ',');
            }

            Activity activity = new Activity();
            activity.ChannelId = "emulator";

            activity.ChannelData = new Dictionary<string, string>()
            {
                {"clientActivityId", Guid.NewGuid().ToString().Replace("-", "")}
            };

            activity.ServiceUrl = e.WebTest.Context["BotConnectorBaseUrl"].ToString();

            activity.Timestamp = DateTime.UtcNow;

            activity.Conversation =
                new ConversationAccount() {Id = e.WebTest.Context[Constants.Context_ConvId].ToString()};
            activity.Text = e.WebTest.Context[Constants.Context_Utterance].ToString();

            string userId;
            if (e.WebTest.Context.ContainsKey(Constants.Context_UserId))
            {
                userId = e.WebTest.Context[Constants.Context_UserId].ToString();
            }
            else
            {
                userId = Helpers.GetUserId();
                e.WebTest.Context[Constants.Context_UserId] = userId;
            }

            activity.From = new ChannelAccount()
            {
                Id = userId,
                Name = userId
            };

            activity.Locale = "en-US";
            activity.Recipient = new ChannelAccount(e.WebTest.Context["BotId"].ToString(), "Bot");
            activity.Type = "message";

            string jsonInput = JsonConvert.SerializeObject(activity,
                new JsonSerializerSettings() {NullValueHandling = NullValueHandling.Ignore});
            e.WebTest.Context[Constants.Context_UserActivity] = jsonInput;

            e.IsMet = true;
        }
    }

    public class PrepareTest : ConditionalRule
    {
        public string TestName { get; set; }

        public string StorageAccountName { get; set; }

        public string StorageAccountKey { get; set; }

        public override void CheckCondition(object sender, ConditionalEventArgs e)
        {
            try
            {
                WebTestContext context = e.WebTest.Context;

                e.IsMet = ReportHelper.Init(StorageAccountName, StorageAccountKey, TestName);
                e.Message = "Azure Storage " + (e.IsMet ? "" : "not") + "found";
            }
            catch
            {
                e.IsMet = false;
            }
        }
    }

    [DisplayName("Message Extraction and Prepare Activity ")]
    [Description("Extracts the messageId and Prepare Activity")]
    public class ExtractMessageIdAndPrepareActivity : ExtractionRule
    {
        public string ParamToExtract { get; set; }

        public override void Extract(object sender, ExtractionEventArgs e)
        {
            JsonExtractionRule msgIdExtractionRule =
                new JsonExtractionRule() { ContextParameterName = this.ContextParameterName, Name = ParamToExtract };

            msgIdExtractionRule.Extract(sender, e);

            WebTestContext context = e.WebTest.Context;

            try
            {
                if (context.ContainsKey(Constants.Context_MessageId) &&
                    context.ContainsKey(Constants.Context_UserActivity))
                {
                    string jsonInput = context[Constants.Context_UserActivity].ToString();
                    Activity activity = JsonConvert.DeserializeObject<Activity>(jsonInput);
                    activity.Conversation =
                        new ConversationAccount() { Id = context[Constants.Context_ConvId].ToString() };

                    activity.Id = context[Constants.Context_MessageId].ToString();

                    context[Constants.Context_UserActivity] = JsonConvert.SerializeObject(activity);
                    int waterMark = int.Parse(context[Constants.Context_MessageId].ToString().Split('|')[1]);
                    if (waterMark > 1)
                    {
                        waterMark--;
                    }

                    context[Constants.Context_Watermark] = $"/{waterMark}";
                    e.Success = true;
                }
                else
                {
                    e.Success = false;
                    e.Message = "Message Id or User Activity not found";
                }
            }
            catch
            {
                e.Success = false;
            }
        }
    }

    [DisplayName("Conditional rule - Prepare SendOnly Activity Data")]
    [Description("Conditional rule to Prepare SendOnly Activity Data")]
    public class PrepareSendOnlyActivityData : ConditionalRule
    {
        public override void CheckCondition(object sender, ConditionalEventArgs e)
        {
            string dataSourceName = e.WebTest.Context["DataSourceName"].ToString();
            string dataSourceTableName = e.WebTest.Context["DataSourceTableName"].ToString();
            string dsPath = $"{dataSourceName}.{dataSourceTableName}";
            List<string> fields = e.WebTest.Context["DataFieldsToExtract"].ToString().Split(',').ToList();
            foreach (var field in fields)
            {
                //{{Test.TestData#csv.Utterance}}
                string fieldName = $"{dsPath}.{field}";

                e.WebTest.Context[field] = e.WebTest.Context[fieldName].ToString().Replace(';', ',');
            }

            string convId = e.WebTest.Context[Constants.Context_ConvId].ToString();

            Activity activity = new Activity();
            activity.ChannelId = "emulator";
            activity.ChannelData = new Dictionary<string, string>()
            {
                {"clientActivityId", Guid.NewGuid().ToString().Replace("-", "")}
            };
            activity.ServiceUrl = e.WebTest.Context["BotConnectorBaseUrl"].ToString();
            activity.Timestamp = DateTime.UtcNow;
            activity.Conversation = new ConversationAccount() { Id = convId };
            activity.Text = e.WebTest.Context[Constants.Context_Utterance].ToString();

            string userId;
            if (e.WebTest.Context.ContainsKey(Constants.Context_UserId))
            {
                userId = e.WebTest.Context[Constants.Context_UserId].ToString();
            }
            else
            {
                userId = Helpers.GetUserId();
                e.WebTest.Context[Constants.Context_UserId] = userId;
            }

            activity.From = new ChannelAccount()
            {
                Id = userId,
                Name = userId
            };

            int activityId = -1;
            if (e.WebTest.Context.ContainsKey(Constants.Context_MessageId))
            {
                activityId = int.Parse(e.WebTest.Context[Constants.Context_MessageId].ToString());
            }

            activityId += 2;

            activity.Id = $"{convId}|{activityId.ToString()}";

            activity.Locale = "en-US";
            activity.Recipient = new ChannelAccount(e.WebTest.Context["BotId"].ToString(), "Bot");
            activity.Type = "message";

            string jsonInput = JsonConvert.SerializeObject(activity,
                new JsonSerializerSettings() { NullValueHandling = NullValueHandling.Ignore });
            e.WebTest.Context[Constants.Context_UserActivity] = jsonInput;

            ReportHelper.WriteLog(convId,
                e.WebTest.Context[Constants.Context_Utterance].ToString(),
                activity.Id,
                userId,
                e.WebTest.Context[Constants.Context_ExpectedResult].ToString(),
                "Preparing",
                string.Empty,
                string.Empty,
                "0",
                "0",
                e.WebTest.Context[Constants.Context_BusinessArea].ToString(),
                e.WebTest.Context[Constants.Context_LuisQnA].ToString());

            e.IsMet = true;
        }
    }

    public class CustomPlugin : WebTestPlugin
    {
        public override void PreWebTest(object sender, PreWebTestEventArgs e)
        {
            if (e.WebTest.Context.ContainsKey("$LoadTestUserContext"))
            {
                LoadTestUserContext loadTestUserContext =
                    e.WebTest.Context["$LoadTestUserContext"] as LoadTestUserContext;

                if (loadTestUserContext.ContainsKey(Constants.Context_ConvId))
                {
                    e.WebTest.Context[Constants.Context_ConvId] = loadTestUserContext[Constants.Context_ConvId].ToString();
                }

                if (loadTestUserContext.ContainsKey(Constants.Context_UserId))
                {
                    e.WebTest.Context[Constants.Context_UserId] = loadTestUserContext[Constants.Context_UserId].ToString();
                }

                if (loadTestUserContext.ContainsKey(Constants.Context_AccessToken))
                {
                    e.WebTest.Context[Constants.Context_AccessToken] = loadTestUserContext[Constants.Context_AccessToken].ToString();
                }
            }
            else
            {
                if (!string.IsNullOrEmpty(Helpers.ConvId))
                    e.WebTest.Context[Constants.Context_ConvId] = Helpers.ConvId;

                if (!string.IsNullOrEmpty(Helpers.AccessToken))
                    e.WebTest.Context[Constants.Context_AccessToken] = Helpers.AccessToken;
            }

            string dataSourceName = e.WebTest.Context["UseDataSource"].ToString();
            foreach (var ds in e.WebTest.DataSources)
            {
                if (ds.Name.Equals(dataSourceName, StringComparison.OrdinalIgnoreCase))
                {
                    e.WebTest.MoveDataTableCursor(ds.Name, ds.Tables[0].Name);
                    e.WebTest.Context["DataSourceName"] = ds.Name;
                    e.WebTest.Context["DataSourceTableName"] = ds.Tables[0].Name;
                    break;
                }
            }

            if (!e.WebTest.Context.ContainsKey("JwtToken"))
            {
                App app = new App()
                {
                    AppId = e.WebTest.Context["BotAppId"].ToString(),
                    AppKey = e.WebTest.Context["BotAppKey"].ToString()
                };
                e.WebTest.Context["JwtToken"] = Helpers.GetJwtToken(app);
            }
        }

        public override void PostWebTest(object sender, PostWebTestEventArgs e)
        {
            base.PostWebTest(sender, e);

            if (e.WebTest.Context.ContainsKey("$LoadTestUserContext"))
            {
                LoadTestUserContext loadTestUserContext =
                    e.WebTest.Context["$LoadTestUserContext"] as LoadTestUserContext;

                if (e.WebTest.Context.ContainsKey(Constants.Context_ConvId))
                {
                    loadTestUserContext[Constants.Context_ConvId] = e.WebTest.Context[Constants.Context_ConvId].ToString();
                }

                if (e.WebTest.Context.ContainsKey(Constants.Context_UserId))
                {
                    loadTestUserContext[Constants.Context_UserId] = e.WebTest.Context[Constants.Context_UserId].ToString();
                }

                if (e.WebTest.Context.ContainsKey(Constants.Context_AccessToken))
                {
                    loadTestUserContext[Constants.Context_AccessToken] = e.WebTest.Context[Constants.Context_AccessToken].ToString();
                }
            }
            else
            {
                Helpers.ConvId = e.WebTest.Context[Constants.Context_ConvId].ToString();
                Helpers.AccessToken = e.WebTest.Context[Constants.Context_AccessToken].ToString();
            }

            string testStatus = e.WebTest.Context.ContainsKey(Constants.Context_TestStatus)
                ? e.WebTest.Context[Constants.Context_TestStatus].ToString()
                : "false";

            if (!e.WebTest.Context.ContainsKey(Constants.Context_MessagePostedToBot))
            {
                e.WebTest.Context[Constants.Context_ActualResult] = "Not able to submit message";
                testStatus = "false";
            }

            string convId = e.WebTest.Context.ContainsKey(Constants.Context_ConvId)
                ? e.WebTest.Context[Constants.Context_ConvId].ToString()
                : string.Empty;
            if (string.IsNullOrEmpty(convId))
            {
                convId = "conversation couldn't be created";
            }

            if (e.WebTest.Context.ContainsKey(Constants.Context_MessageId) &&
                !string.IsNullOrEmpty(e.WebTest.Context[Constants.Context_MessageId].ToString()))
            {
                string utterance = e.WebTest.Context[Constants.Context_Utterance].ToString();
                string expectedResult = e.WebTest.Context[Constants.Context_ExpectedResult].ToString();
                string luisQnA = e.WebTest.Context[Constants.Context_LuisQnA].ToString();
                string businessArea = e.WebTest.Context[Constants.Context_BusinessArea].ToString();

                string actual = e.WebTest.Context.ContainsKey(Constants.Context_ActualResult)
                    ? e.WebTest.Context[Constants.Context_ActualResult].ToString()
                    : string.Empty;
                string testMatch = e.WebTest.Context.ContainsKey(Constants.Context_TestMatch)
                    ? e.WebTest.Context[Constants.Context_TestMatch].ToString()
                    : "false";
                string duration = e.WebTest.Context.ContainsKey(Constants.Context_Duration)
                    ? e.WebTest.Context[Constants.Context_Duration].ToString()
                    : "0";
                string message = e.WebTest.Context.ContainsKey(Constants.Context_TestStatusMessage)
                    ? e.WebTest.Context[Constants.Context_TestStatusMessage].ToString()
                    : string.Empty;
                string activityCount = e.WebTest.Context.ContainsKey(Constants.Context_ActivityCount)
                    ? e.WebTest.Context[Constants.Context_ActivityCount].ToString()
                    : "0";
                string messageId = e.WebTest.Context.ContainsKey(Constants.Context_MessageId)
                    ? e.WebTest.Context[Constants.Context_MessageId].ToString()
                    : "Not able to submit messsage";

                string userId = e.WebTest.Context.ContainsKey(Constants.Context_UserId)
                    ? e.WebTest.Context[Constants.Context_UserId].ToString()
                    : "User XX";

                if (!string.IsNullOrEmpty(message))
                {
                    actual = message;
                }

                e.WebTest.Outcome = testStatus.Equals("true", StringComparison.OrdinalIgnoreCase)
                    ? Outcome.Pass
                    : Outcome.Fail;

                ReportHelper.WriteLog(convId, utterance, messageId, userId, expectedResult, actual, testStatus, testMatch,
                    duration, activityCount, businessArea, luisQnA);
            }
        }
    }
}