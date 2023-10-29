using LumosLIB.Kernel;
using LumosLIB.Kernel.Log;
using LumosLIB.Tools;
using LumosProtobuf;
using MQTTnet;
using MQTTnet.Client;
using System.Text;
using T = LumosLIB.Tools.I18n.DummyT;

namespace org.dmxc.lumos.Kernel.Input.v2.Worker
{
    public class MqttClientNode : AbstractNodeWithEnable
    {
        protected static readonly ILumosLog log = LumosLogger.getInstance(typeof(MqttClientNode));

        public static readonly string NAME = T._("MQTT Client");
        public static readonly string TYPE = "__MQTT_CLIENT";

        private const string P_HOSTADDRESS = "HostAddress";
        private const string P_USERNAME = "Username";
        private const string P_PASSWORD = "Password";
        private const string P_PORT = "Port";
        private const string P_TOPIC = "Topic";

        private IGraphNodeOutputPort outputMessage;

        private MqttClient client;

        public MqttClientNode(GraphNodeID id)
            : base(id, TYPE, GetWrapperCategory(T._("MQTT")))
        {
            this.Name = NAME;
        }
        private static ParameterCategory GetWrapperCategory(params string[] subs)
        {
            var a = KnownCategories.WRAPPER;
            a.SubCategory = ParameterCategoryTools.FromNames(subs);
            return a;
        }

        protected override bool DefaultEnableValue => false;

        private string HostAddress { get; set; } = "127.0.0.1";
        private string Username { get; set; } = "dmxcontrol";
        private string Password { get; set; } = "dmxcontrolcantalkwithmosquitto";
        private int Port { get; set; } = 1883;
        private string Topic { get; set; } = "/Empty";

        protected override void OnAddedToGraph()
        {
            base.OnAddedToGraph();
            AssignEvents(true);
        }

        protected override void OnRemovedFromGraph()
        {
            base.OnRemovedFromGraph();
            AssignEvents(false);
        }

        protected override void AddDefaultPortsInternal()
        {
            base.AddDefaultPortsInternal();
            outputMessage = Outputs.SingleOrDefault(c => c.Name == "Message") ??
                            AddOutputPort(name: "Message");
            outputMessage.ShortName = T._("Message");
        }

        protected override void DisposeHook()
        {
            base.DisposeHook();
            AssignEvents(false);
        }

        private void AssignEvents(bool register)
        {

        }

        protected override void EnabledChangedHook()
        {
            base.EnabledChangedHook();

            if (Enable)
            {
                CloseClient();

                Task.Run(async () =>
                {
                    try
                    {
                        var mqttFactory = new MqttFactory();
                        client = (MqttClient)mqttFactory.CreateMqttClient();
                        var mqttClientOptions = new MqttClientOptionsBuilder().WithTcpServer(HostAddress).WithCredentials(Username, Password).Build();
                        client.DisconnectedAsync += async e =>
                        {
                            log.Info($"Connection lost {HostAddress} Topic: {Topic}");
                            if (e.ClientWasConnected)
                                {
                                MqttClientConnectResult res = null;
                                do
                                {
                                    if (res != null)
                                        await Task.Delay(1000);
                                    // Use the current options as the new options.
                                    res = await client.ConnectAsync(client.Options);
                                }
                                while (res.ResultCode != MqttClientConnectResultCode.Success);
                                log.Info($"Reconnected to {HostAddress} Topic: {Topic}");

                            }
                            };
                        MqttClientConnectResult response = null;
                        do
                        {
                            if (response != null)
                                await Task.Delay(1000);
                            response = await client.ConnectAsync(mqttClientOptions, CancellationToken.None);
                        }
                        while (response.ResultCode != MqttClientConnectResultCode.Success);
                        log.Info($"Connected to {HostAddress} Topic: {Topic}");
                        var mqttSubscribeOptions = mqttFactory.CreateSubscribeOptionsBuilder()
                            .WithTopicFilter(
                                f =>
                                {
                                    f.WithTopic($"{Topic}/{2}");
                                })
                            .Build();
                        client.ApplicationMessageReceivedAsync += Client_ApplicationMessageReceivedAsync;
                        await client.SubscribeAsync(mqttSubscribeOptions, CancellationToken.None);

                        log.Debug($"Subscribed to {HostAddress} Topic: {Topic}");
                    }
                    catch (Exception e)
                    {
                        log.Warn("Unable to enable MQTT Client", e);
                        CloseClient();
                    }
                });
            }
            else
            {
                CloseClient();
            }

            async void CloseClient()
            {
                if (client == null) return;

                try
                {
                    await client.DisconnectAsync(new MqttClientDisconnectOptionsBuilder().WithReason(MqttClientDisconnectOptionsReason.NormalDisconnection).Build());
                    client.ApplicationMessageReceivedAsync -= Client_ApplicationMessageReceivedAsync;
                }
                catch (Exception e)
                {
                    log.ErrorOrDebug(e);
                }
                finally
                {
                    client = null;
                }
            }
        }

        private async Task Client_ApplicationMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
        {
            string message = Encoding.UTF8.GetString(e.ApplicationMessage.Payload);
            log.Debug("MQTT Received from Topic {0}: {1}", e.ApplicationMessage.Topic, message);
            outputMessage.Value = message;
            OnProcessRequested();
        }

        #region Parameters

        protected override IEnumerable<GenericParameter> ParametersInternal
        {
            get
            {
                yield return new GenericParameter(P_HOSTADDRESS, GraphNodeParameters.GraphNodeParameterType, typeof(string));
                yield return new GenericParameter(P_USERNAME, GraphNodeParameters.GraphNodeParameterType, typeof(string));
                yield return new GenericParameter(P_PASSWORD, GraphNodeParameters.GraphNodeParameterType, typeof(string));
                yield return new GenericParameter(P_PORT, GraphNodeParameters.GraphNodeParameterType, typeof(ushort));
                yield return new GenericParameter(P_TOPIC, GraphNodeParameters.GraphNodeParameterType, typeof(string));
            }
        }
        protected override object getParameterInternal(GenericParameter parameter)
        {
            switch (parameter.Name)
            {
                case P_HOSTADDRESS: return this.HostAddress;
                case P_USERNAME: return this.Username;
                case P_PASSWORD: return this.Password;
                case P_PORT: return this.Port;
                case P_TOPIC: return this.Topic;
            }
            return base.getParameterInternal(parameter);
        }

        protected override bool setParameterInternal(GenericParameter parameter, object value)
        {
            switch (parameter.Name)
            {
                case P_HOSTADDRESS:
                    if (!(value is string s)) return false;
                    this.HostAddress = s;
                    return true;

                case P_USERNAME:
                    if (!(value is string s2)) return false;
                    this.Username = s2;
                    return true;

                case P_PASSWORD:
                    if (!(value is string s3)) return false;
                    this.Password = s3;
                    return true;

                case P_PORT:
                    if (!LumosTools.TryConvertToInt32(value, out var i)) return false;
                    this.Port = i;
                    return true;

                case P_TOPIC:
                    if (!(value is string t)) return false;
                    this.Topic = t;
                    return true;
            }
            return base.setParameterInternal(parameter, value);
        }

        protected override bool testParameterInternal(GenericParameter parameter, object value)
        {
            switch (parameter.Name)
            {
                case P_PORT:
                    return LumosTools.TryConvertToInt32(value, out _);

                case P_HOSTADDRESS:
                case P_USERNAME:
                case P_PASSWORD:
                case P_TOPIC:
                    return value is string;
            }

            return base.testParameterInternal(parameter, value);
        }
        #endregion
    }
}
