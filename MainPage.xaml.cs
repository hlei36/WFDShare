using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Devices.Enumeration;
using Windows.Devices.WiFiDirect;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Networking;
using Windows.Networking.Connectivity;
using Windows.Networking.Sockets;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.System;
using Windows.System.Profile;
using Windows.UI.Core;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x804 上介绍了“空白页”项模板

namespace WFDShare
{
    /// <summary>
    /// 可用于自身或导航至 Frame 内部的空白页。
    /// </summary>
    public sealed partial class MainPage : Page
    {

        public DeviceWatcher _deviceWatcher = null;
        public WiFiDirectAdvertisementPublisher publisher = null;
        public WiFiDirectConnectionListener listener = null;
        public ObservableCollection<DiscoveredDevice> discoveredDevices { get; } = new ObservableCollection<DiscoveredDevice>();
        public ObservableCollection<StorageFile> Files { get; set; } = new ObservableCollection<StorageFile>();
        WiFiDirectDevice wfdDevice = null;

        uint bufferSize = 1 * 68 * 1024;
        int scanTimes = 0;

        public MainPage()
        {
            this.InitializeComponent();
            getLocalName();
            startPublish();
            sendfile.DragOver += FileListView_DragOver;
            sendfile.Drop += FileListView_Drop;

        }

        //确保我们只接受包含存储项的数据包
        private void FileListView_DragOver(object sender, DragEventArgs e)
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
            }
            else
            {
                e.AcceptedOperation = DataPackageOperation.None;
            }
        }

        //获取拖拽的数据，并将其添加到ListView的数据源中
        private async void FileListView_Drop(object sender, DragEventArgs e)
        {
            bool chongfu = false;
            this.StatusTextBlock.Text = "";
            ObservableCollection<StorageFile> chongfuFiles = new ObservableCollection<StorageFile>();
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                var files = await e.DataView.GetStorageItemsAsync();
                foreach(var file in files)
                { 
                    if (file is StorageFolder)
                    {
                        this.StatusTextBlock.Text = "再不支持文件夹";
                        return;
                    }
                }
                   
                foreach (StorageFile file in files)
                {
                    // 检查文件是否已经存在于 Files 集合中                    
                    if (!Files.Any(f => f.Name == file.Name && f.Path == file.Path))
                    {
                        Files.Add(file);
                    }
                    else
                    {
                        chongfu = true;
                        chongfuFiles.Add(file);
                    }
                    
                }
                if (chongfu)
                { 
                    string chongfuNames = null;
                    foreach (StorageFile file in chongfuFiles)
                    {
                        chongfuNames += file.Name;
                        chongfuNames += "/";
                    }
                    await this.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        StatusTextBlock.Text = $"有{chongfuFiles.Count}个文件重复："+chongfuNames;

                    });
                }

            }
        }

        public async void getLocalName()
        {
            string name = System.Environment.MachineName;
            if (!string.IsNullOrEmpty(name))
            {
                await this.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    LocalName.Text = name;
                });
            }
        }

        //启动wifidirect发现扫描
        public void startWatch()
        {
            String deviceSelector = WiFiDirectDevice.GetDeviceSelector(WiFiDirectDeviceSelectorType.AssociationEndpoint);
            _deviceWatcher = DeviceInformation.CreateWatcher(deviceSelector, new string[] { "System.Devices.WiFiDirect.InformationElements" });
            discoveredDevices.Clear();
            _deviceWatcher.Added += _deviceWatcher_Added;
            _deviceWatcher.Updated += _deviceWatcher_Updated;
            _deviceWatcher.Removed += _deviceWatcher_Removed;
            _deviceWatcher.Stopped += _deviceWatcher_Stopped;
            _deviceWatcher.EnumerationCompleted += _deviceWatcher_EnumerationCompleted;
            _deviceWatcher.Start();
        }

        public void stopWatch()
        {
            _deviceWatcher.Added -= _deviceWatcher_Added;
            _deviceWatcher.Updated -= _deviceWatcher_Updated;
            _deviceWatcher.EnumerationCompleted -= _deviceWatcher_EnumerationCompleted;
            _deviceWatcher.Removed -= _deviceWatcher_Removed;
            _deviceWatcher.Stopped -= _deviceWatcher_Stopped;
            _deviceWatcher.Stop();
            _deviceWatcher = null;
            discoveredDevices.Clear();
        }

        public void startPublish()
        {
            discoveredDevices.Clear();
            publisher = new WiFiDirectAdvertisementPublisher();
            publisher.Advertisement.ListenStateDiscoverability = WiFiDirectAdvertisementListenStateDiscoverability.Normal;
            publisher.Advertisement.IsAutonomousGroupOwnerEnabled = false;
            listener = new WiFiDirectConnectionListener();
            listener.ConnectionRequested += Listener_ConnectionRequested;
            publisher.Start();
        }

        public void stopPublish()
        {
            publisher.Stop();
            publisher = null;
            listener.ConnectionRequested -= Listener_ConnectionRequested;
            listener = null;
        }


        private async void Listener_ConnectionRequested(WiFiDirectConnectionListener sender, WiFiDirectConnectionRequestedEventArgs args)
        {
            //响应其他设备的连接请求
            WiFiDirectConnectionRequest wiFiDirectConnectionRequest = args.GetConnectionRequest();
            bool success = await Dispatcher.RunTaskAsync(async () =>
            {
                return await HandleConnectionRequestAsync(wiFiDirectConnectionRequest);
            });
        }

        private async void _deviceWatcher_Added(DeviceWatcher sender, DeviceInformation args)
        {
            bool deviceAdded = false;
            await this.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                foreach (DiscoveredDevice device in discoveredDevices)
                {
                    if (device.DeviceInfo.Id == args.Id)
                    {
                        deviceAdded = true;
                        break;
                    }
                }
            });
            if (!deviceAdded)
            {
                await this.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    discoveredDevices.Add(new DiscoveredDevice(args));
                });
            }
            
        }

        private async void _deviceWatcher_Updated(DeviceWatcher sender, DeviceInformationUpdate args)
        {
            await this.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                foreach (DiscoveredDevice device in discoveredDevices)
                {
                    if (device.DeviceInfo.Id == args.Id)
                    {
                        device.UpdateDeviceInfo(args);
                        break;
                    }
                }
            });
        }


        private async void _deviceWatcher_Removed(DeviceWatcher sender, DeviceInformationUpdate args)
        {
            await this.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                foreach (DiscoveredDevice device in discoveredDevices)
                {
                    if (device.DeviceInfo.Id == args.Id)
                    {
                        discoveredDevices.Remove(device);
                        break;
                    }
                }
            });
        }



        private async void _deviceWatcher_Stopped(DeviceWatcher sender, object args)
        {
            await this.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                StatusTextBlock.Text = "扫描已停止！" + _deviceWatcher.Status;
            });

        }

        private async void _deviceWatcher_EnumerationCompleted(DeviceWatcher sender, object args)
        {
            
            if (_deviceWatcher.Status == DeviceWatcherStatus.EnumerationCompleted)
            {
                _deviceWatcher.Stop();
                
                await Task.Delay(500);
                _deviceWatcher.Start();
                await this.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    scanTimes++;
                    StatusTextBlock.Text = $"第{scanTimes}次扫描";
                });

            }
            
        }

        private async void Scan_Click(object sender, RoutedEventArgs e)
        {

            await this.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                this.StatusTextBlock.Text = "";
            });
            if (btscan.Content.ToString() == "扫描")
            {
                //停止发现状态
                if (publisher?.Status == WiFiDirectAdvertisementPublisherStatus.Started)
                {
                    stopPublish();
                }
                //执行扫描发现其他wifidirect设备
                if (_deviceWatcher == null || _deviceWatcher.Status == DeviceWatcherStatus.Stopped)
                {
                    startWatch();
                }
                //content变为停止扫描
                btscan.Content = "停止扫描";
            }
            else
            {
                //停止扫描
                if (_deviceWatcher.Status == DeviceWatcherStatus.Started)
                {
                    stopWatch();
                    scanTimes = 0;
                }
                //并处于可被发现状态
                if (publisher == null || publisher.Status == WiFiDirectAdvertisementPublisherStatus.Stopped)
                {
                    startPublish();
                }

                //content变为扫描
                btscan.Content = "扫描";
            }
        }

        private async Task<bool> HandleConnectionRequestAsync(WiFiDirectConnectionRequest wiFiDirectConnectionRequest)
        {
            string deviceName = wiFiDirectConnectionRequest.DeviceInformation.Name;
            bool isPaired = (wiFiDirectConnectionRequest.DeviceInformation.Pairing?.IsPaired == true) ||
                (await IsAepPairedAsync(wiFiDirectConnectionRequest.DeviceInformation.Id));

            // Show the prompt提示 only in case of WiFiDirect reconnection or Legacy client connection.
            if (isPaired || publisher.Advertisement.LegacySettings.IsEnabled)
            {
                var messageDialog = new MessageDialog($"来自 {deviceName}的文件发送请求", "设备配对和文件发送请求");

                // Add two commands, distinguished by their tag.
                // The default command is "Decline", and if the user cancels, we treat it as "Decline".
                messageDialog.Commands.Add(new UICommand("接受", null, true));
                messageDialog.Commands.Add(new UICommand("拒绝", null, null));
                messageDialog.DefaultCommandIndex = 1;
                messageDialog.CancelCommandIndex = 1;

                // Show the message dialog
                var commandChosen = await messageDialog.ShowAsync();

                if (commandChosen.Id == null)
                {
                    return false;
                }
            }
            if (!isPaired)
            {
                bool success = await RequestPairDeviceAsync(wiFiDirectConnectionRequest.DeviceInformation.Pairing, 8);
                if (!success)
                {
                    return false;
                }
            }

            bool serverStartSccess = false;
            await this.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
            {
                try
                {
                    // IMPORTANT: FromIdAsync needs to be called from the UI thread               
                    this.StatusTextBlock.Text = "正在启动服务器...";
                    wfdDevice = await WiFiDirectDevice.FromIdAsync(wiFiDirectConnectionRequest.DeviceInformation.Id);
                    var listenerSocket = new StreamSocketListener();
                    var EndpointPairs = wfdDevice.GetConnectionEndpointPairs();
                    listenerSocket.ConnectionReceived += ListenerSocket_ConnectionReceived;
                    await listenerSocket.BindEndpointAsync(EndpointPairs[0].LocalHostName, "20001");
                    this.StatusTextBlock.Text = "服务器启动成功!";
                    serverStartSccess = true;
                }
                catch (Exception ex)
                {
                    await this.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        this.StatusTextBlock.Text = "连接及服务器启动失败" + ex.Message;
                    });
                    serverStartSccess = false;
                }
            });
            return serverStartSccess;
        }

        //于检查一个特定的设备（通过其AEP ID标识）是否已与系统配对。AEP（Advanced Endpoint Discovery Protocol）是一种用于发现和配对设备的技术。
        private async Task<bool> IsAepPairedAsync(string deviceId)
        {
            List<string> additionalProperties = new List<string>();
            additionalProperties.Add("System.Devices.Aep.DeviceAddress");
            String deviceSelector = $"System.Devices.Aep.AepId:=\"{deviceId}\"";
            DeviceInformation devInfo = null;

            try
            {
                devInfo = await DeviceInformation.CreateFromIdAsync(deviceId, additionalProperties);
            }
            catch (Exception ex)
            {
                await this.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    this.StatusTextBlock.Text = "DeviceInformation.CreateFromIdAsync threw an exception: " + ex.Message;
                });
                //rootPage.NotifyUser("DeviceInformation.CreateFromIdAsync threw an exception: " + ex.Message, NotifyType.ErrorMessage);
            }

            if (devInfo == null)
            {
                await this.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    this.StatusTextBlock.Text = "设备信息为空";
                });
                //rootPage.NotifyUser("Device Information is null", NotifyType.ErrorMessage);
                return false;
            }

            deviceSelector = $"System.Devices.Aep.DeviceAddress:=\"{devInfo.Properties["System.Devices.Aep.DeviceAddress"]}\"";
            DeviceInformationCollection pairedDeviceCollection = await DeviceInformation.FindAllAsync(deviceSelector, null, DeviceInformationKind.Device);
            return pairedDeviceCollection.Count > 0;
        }

        //接收文件
        private  async void ListenerSocket_ConnectionReceived(StreamSocketListener sender, StreamSocketListenerConnectionReceivedEventArgs args)
        {

            int receivedFilesCount = 0;
            using (StreamSocket socket = args.Socket)
            {
                
                using (var inputStream = socket.InputStream)
                {
                    using (var outputStream = socket.OutputStream)
                    {
                        using (var reader = new DataReader(inputStream))
                        using (var writer = new DataWriter(outputStream))
                        {
                            receivedFilesCount = await ReceiveFileAsync(reader);
                        }
                    }
                }
                
            }
            await this.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                this.StatusTextBlock.Text = $"已收到{receivedFilesCount}个文件,保存在{UserDataPaths.GetDefault().Downloads}";
            });
            //var t = new FolderLauncherOptions();
            //StorageFolder folder = await StorageFolder.GetFolderFromPathAsync(UserDataPaths.GetForUser().Downloads);
            //await Launcher.LaunchFolderAsync(folder, t);
            sender.Dispose();
            wfdDevice.Dispose();
            wfdDevice = null;
        }


        private async Task<int> ReceiveFileAsync(DataReader reader)
        {
            reader.UnicodeEncoding = Windows.Storage.Streams.UnicodeEncoding.Utf8;
            reader.ByteOrder = ByteOrder.BigEndian;
            int receivedFilesCount = 0;
            int filesCount = 0;

            //接收文件数量
            await reader.LoadAsync(sizeof(uint));
            filesCount = (int)reader.ReadUInt32();
            

            for (int i =  0; i < filesCount; i++)
            {
                // 读取文件名
                string fileName;
                await reader.LoadAsync(sizeof(uint));
                uint fileNamelength = reader.ReadUInt32();
                await reader.LoadAsync(fileNamelength);
                byte[] fileNameByte = new byte[fileNamelength];
                reader.ReadBytes(fileNameByte);
                fileName = Encoding.UTF8.GetString(fileNameByte);


                // 创建文件并写入数据
                StorageFile file = await DownloadsFolder.CreateFileAsync(fileName, CreationCollisionOption.GenerateUniqueName);
                
                using (var fileStream = await file.OpenAsync(FileAccessMode.ReadWrite))
                {

                    //读取文件长度
                    uint fileLength = await reader.LoadAsync(sizeof(uint));
                    uint length = reader.ReadUInt32();

                    //定义缓存大小
                    //uint bufferSize = 64 * 1024;

                    //计算读取缓存的次数
                    int readTimes = (int)(length / (int)bufferSize);
                    uint lastBytesLength = (uint)(length % (int)bufferSize);

                    DateTimeOffset startTime = DateTimeOffset.Now;
                    DateTimeOffset currentTime = DateTimeOffset.Now;
                    TimeSpan elapsed = currentTime - startTime;
                    int totalBytesSent = 0;
                    int BytesSent = 0;

                    var buffer = new byte[bufferSize];
                    while (readTimes > 0)
                    {
                        currentTime = DateTimeOffset.Now;
                        await reader.LoadAsync(bufferSize);                        
                        reader.ReadBytes(buffer);
                        IBuffer buffer1 = buffer.AsBuffer();
                        await fileStream.WriteAsync(buffer1);
                        BytesSent += buffer.Length;
                        readTimes--;
                        elapsed = currentTime - startTime;
                        if (elapsed.TotalSeconds > 2)
                        {
                            startTime = currentTime;
                            totalBytesSent += BytesSent;
                            await this.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                            {
                                this.StatusTextBlock.Text = (BytesSent / elapsed.TotalSeconds / 1024 / 1024).ToString("N2", CultureInfo.InvariantCulture) + "MB/s,已接收"
                                     + ((float)totalBytesSent * 100 / length).ToString("N1", CultureInfo.InvariantCulture) + "%";
                            });
                            BytesSent = 0;
                        }                        
                    }

                    var lastBuffer = new byte[lastBytesLength];
                    await reader.LoadAsync(lastBytesLength);
                    reader.ReadBytes(lastBuffer);
                    IBuffer lastBuffer1 = lastBuffer.AsBuffer();
                    await fileStream.WriteAsync(lastBuffer1);
                    receivedFilesCount++;
                }
            }

            //await Launcher.LaunchFolderAsync(SystemDataPaths., new FolderLauncherOptions()) ;
            return receivedFilesCount;
            
        }  

        //设置配对参数完成配对
        public async Task<bool> RequestPairDeviceAsync(DeviceInformationPairing pairing,short intent)
        {
            //配对参数
            WiFiDirectConnectionParameters connectionParams = new WiFiDirectConnectionParameters();
            connectionParams.GroupOwnerIntent = intent;
            connectionParams.PreferenceOrderedConfigurationMethods.Add(WiFiDirectConfigurationMethod.PushButton);
            connectionParams.PreferredPairingProcedure = WiFiDirectPairingProcedure.GroupOwnerNegotiation;

            DevicePairingKinds devicePairingKinds = DevicePairingKinds.ConfirmOnly;// | DevicePairingKinds.DisplayPin | DevicePairingKinds.ProvidePin;//配对方式

            //配对设备
            DeviceInformationCustomPairing customPairing = pairing.Custom;
            customPairing.PairingRequested += OnPairingRequested;

            DevicePairingResult result = await customPairing.PairAsync(devicePairingKinds, DevicePairingProtectionLevel.None, connectionParams);
            await this.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                this.StatusTextBlock.Text = result.Status.ToString();
            });
            if (result.Status != DevicePairingResultStatus.Paired && result.Status != DevicePairingResultStatus.AlreadyPaired)
            {
                //rootPage.NotifyUser($"PairAsync failed, Status: {result.Status}", NotifyType.ErrorMessage);
                return false;
            }
            return true;
        }


        private void OnPairingRequested(DeviceInformationCustomPairing sender, DevicePairingRequestedEventArgs args)
        {
            Utils.HandlePairing(Dispatcher, args);
        }

        private async void send_Click(object sender, RoutedEventArgs e)
        {
            int sendedFilesCount = 0;
            if(_deviceWatcher?.Status == DeviceWatcherStatus.Started)
            {
                _deviceWatcher.Stop();
                this.btscan.Content = "扫描";
                scanTimes = 0;
            }
            
            
            if (Files.Count == 0)
            {
                return;
            }

            var discoveredDevice = (DiscoveredDevice)DevicesListView.SelectedItem;//lvDiscoveredDevices选择已发现的对话框

            if (discoveredDevice == null)
            {
                StatusTextBlock.Text = "未选择任何设备";
                return;
            }

           

            if (!discoveredDevice.DeviceInfo.Pairing.IsPaired)//没有配对执行
            {

                if (!await RequestPairDeviceAsync(discoveredDevice.DeviceInfo.Pairing, 2))
                {
                    return;
                }
                else
                {
                    //await this.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    //{
                    //    //discoveredDevices.Add(new DiscoveredDevice(args));
                    //    StatusTextBlock.Text = "配对成功";
                    //    //textbox1.Text += args.Id;
                    //});
                }
            }

            //WiFiDirectDevice wfdDevice = null;
             
            await this.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
            {
                try
                { 
                    wfdDevice = await WiFiDirectDevice.FromIdAsync(discoveredDevice.DeviceInfo.Id);
                    wfdDevice.ConnectionStatusChanged += WfdDevice_ConnectionStatusChanged;
                    IReadOnlyList<EndpointPair> endpointPairs = wfdDevice.GetConnectionEndpointPairs();
                    HostName remoteHostName = endpointPairs[0].RemoteHostName;

                    await Task.Delay(2000);

                    ObservableCollection<StorageFile> FileTemp = new ObservableCollection<StorageFile>(Files);
                    StreamSocket clientSocket = new StreamSocket();
                    clientSocket.Control.NoDelay = true;
                    //clientSocket.Control.OutboundBufferSizeInBytes = 10*1024*1024;

                    try
                    {
                        await clientSocket.ConnectAsync(remoteHostName, "20001");

                        await this.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                        {
                            StatusTextBlock.Text = "连接服务器成功";
                        });
                    }
                    catch (Exception ex)
                    {
                        await this.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                        {
                            this.StatusTextBlock.Text = "发送失败:" + ex.Message;
                        });
                        return;
                    }


                    sendedFilesCount = await SendFileAsync(clientSocket, Files);

                    clientSocket.Dispose();
                    clientSocket = null;
                    StatusTextBlock.Text = $"成功发送{sendedFilesCount}个文件";
                    //断开连接，恢复到广播状态，按钮变成扫描
                }
                catch (Exception ex)
                {
                    await this.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        this.StatusTextBlock.Text = "服务器连接失败:" + ex.Message;
                    });
                }
            });
           

        }



        private async Task<int> SendFileAsync(StreamSocket socket, ObservableCollection<StorageFile> files)
        {
            //复制Files用于发送
            ObservableCollection<StorageFile> fileTemp = new ObservableCollection<StorageFile>(files);
            //获取要发送的文件数量
            int filesCount = fileTemp.Count;
            int sendedFilesCount = 0;
            using (var dataWriter = new DataWriter(socket.OutputStream))
            {
                dataWriter.UnicodeEncoding = Windows.Storage.Streams.UnicodeEncoding.Utf8;
                dataWriter.ByteOrder = ByteOrder.BigEndian;
                //发送文件数量
                dataWriter.WriteUInt32((uint)filesCount);

                //循环发送文件
                foreach(StorageFile file in fileTemp)
                {
                    using (IRandomAccessStream fileStream = await file.OpenAsync(FileAccessMode.Read))//创建文件流
                    {
                        using (var dataReader = new DataReader(fileStream))//创建dataReader
                        {
                            dataReader.UnicodeEncoding = Windows.Storage.Streams.UnicodeEncoding.Utf8;
                            dataReader.ByteOrder = ByteOrder.BigEndian;
                            //读取要发送的文件名及长度
                            string fileNameToSend = Path.GetFileName(file.Name);
                            byte[] fileNameByte = Encoding.UTF8.GetBytes(fileNameToSend);
                            uint fileNmaeLength = (uint)fileNameByte.Length;
                            //读取要发送的文件长度
                            uint fileLength = (uint)fileStream.Size;
                            // 发送文件名及长度
                            dataWriter.WriteUInt32(fileNmaeLength);
                            dataWriter.WriteBytes(fileNameByte);
                            //发送文件长度 
                            dataWriter.WriteUInt32(fileLength);
                            //定义缓冲大小
                            

                            //计算使用缓冲区的次数
                            int readTimes = (int)(fileLength / (int)bufferSize);
                            uint lastBytesSize = (uint)(fileLength % (int)bufferSize);


                            byte[] fileBuffer = new byte[bufferSize];
                            //uint bytesRead;
                            DateTimeOffset startTime = DateTimeOffset.Now;
                            DateTimeOffset currentTime = DateTimeOffset.Now;
                            TimeSpan elapsed = currentTime - startTime;
                            int totalBytesSent = 0;
                            int BytesSent = 0;
                            while (readTimes > 0)
                            {
                                currentTime = DateTimeOffset.Now;
                                await dataReader.LoadAsync(bufferSize);
                                dataReader.ReadBytes(fileBuffer);
                                dataWriter.WriteBytes(fileBuffer);
                                await dataWriter.StoreAsync();
                                await dataWriter.FlushAsync();
                                BytesSent += fileBuffer.Length;
                                readTimes--;
                                elapsed = currentTime - startTime;
                                if (elapsed.TotalSeconds>2)
                                {
                                    startTime = currentTime;
                                    totalBytesSent += BytesSent;
                                    this.StatusTextBlock.Text = (BytesSent / elapsed.TotalSeconds / 1024 / 1024).ToString("N2", CultureInfo.InvariantCulture) + "MB/s,已发送"+((float)totalBytesSent * 100/fileLength).ToString("N2", CultureInfo.InvariantCulture)+"%";
                                    BytesSent = 0;
                                }
                            }
                            var lastBuffer = new byte[lastBytesSize];
                            await dataReader.LoadAsync(lastBytesSize);
                            dataReader.ReadBytes(lastBuffer);
                            dataWriter.WriteBytes(lastBuffer);

                            await dataWriter.StoreAsync();
                            await dataWriter.FlushAsync();
                        }
                    }
                    //在listbox中移除已发送的文件
                    Files.Remove(file);
                    sendedFilesCount++;
                }

                
            }
            return sendedFilesCount;
        }
              
       

       
        private async void WfdDevice_ConnectionStatusChanged(WiFiDirectDevice sender, object args)
        {
            await this.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                StatusTextBlock.Text = sender.ConnectionStatus.ToString();
            });


            //throw new NotImplementedException();
        }

        private void clearFiles_Click(object sender, RoutedEventArgs e)
        {            
            if (Files.Count > 0)
            {
                Files.Clear();
            }
        }

        private async void selectFiles(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker
            {

                // 设置初始位置为下载文件夹
                SuggestedStartLocation = PickerLocationId.Downloads,
                

            };
            picker.FileTypeFilter.Add("*");
            // 显示文件选择器并让用户选择文件
            var files = await picker.PickMultipleFilesAsync();

            // 检查用户是否选择了文件
            if (files != null)
            {
                bool chongfu = false;
                this.StatusTextBlock.Text = "";
                ObservableCollection<StorageFile> chongfuFiles = new ObservableCollection<StorageFile>();
                
                foreach (StorageFile file in files)
                {
                    // 检查文件是否已经存在于 Files 集合中                    
                    if (!Files.Any(f => f.Name == file.Name && f.Path == file.Path))
                    {
                        Files.Add(file);
                    }
                    else
                    {
                        chongfu = true;
                        chongfuFiles.Add(file);
                    }

                }
                if (chongfu)
                {
                    string chongfuNames = null;
                    foreach (StorageFile file in chongfuFiles)
                    {
                        chongfuNames += file.Name;
                        chongfuNames += "/";
                    }
                    await this.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        StatusTextBlock.Text = $"有{chongfuFiles.Count}个文件重复：{chongfuNames}";

                    });
                }
            }
            else
            {
                // 操作被取消
            }
        }

    }


    public static class DispatcherTaskExtensions
    {

        public static async Task<T> RunTaskAsync<T>(this CoreDispatcher dispatcher,
            Func<Task<T>> func, CoreDispatcherPriority priority = CoreDispatcherPriority.Normal)
        {
            var taskCompletionSource = new TaskCompletionSource<T>();
            await dispatcher.RunAsync(priority, async () =>
            {
                try
                {
                    taskCompletionSource.SetResult(await func());
                }
                catch (Exception ex)
                {
                    taskCompletionSource.SetException(ex);
                }
            });
            return await taskCompletionSource.Task;
        }

        // There is no TaskCompletionSource<void> so we use a bool that we throw away.
        public static async Task RunTaskAsync(this CoreDispatcher dispatcher,
            Func<Task> func, CoreDispatcherPriority priority = CoreDispatcherPriority.Normal) =>
            await RunTaskAsync(dispatcher, async () => { await func(); return false; }, priority);
    }


    public static class Utils
    {
        private static async Task ShowPinToUserAsync(CoreDispatcher dispatcher, string strPin)
        {
            await dispatcher.RunTaskAsync(async () =>
            {
                var messageDialog = new MessageDialog($"Enter this PIN on the remote device: {strPin}");

                // Add commands
                messageDialog.Commands.Add(new UICommand("OK", null, 0));

                // Set the command that will be invoked by default 
                messageDialog.DefaultCommandIndex = 0;

                // Set the command that will be invoked if the user cancels
                messageDialog.CancelCommandIndex = 0;

                // Show the Pin 
                await messageDialog.ShowAsync();
            });
        }

        private static async Task<string> GetPinFromUserAsync(CoreDispatcher dispatcher)
        {
            return await dispatcher.RunTaskAsync(async () =>
            {
                var pinBox = new TextBox();
                var dialog = new ContentDialog()
                {
                    Title = "Enter Pin",
                    PrimaryButtonText = "OK",
                    Content = pinBox
                };
                await dialog.ShowAsync();
                return pinBox.Text;
            });
        }

        public static async void HandlePairing(CoreDispatcher dispatcher, DevicePairingRequestedEventArgs args)
        {
            using (Deferral deferral = args.GetDeferral())
            {
                switch (args.PairingKind)
                {
                    case DevicePairingKinds.DisplayPin:
                        await ShowPinToUserAsync(dispatcher, args.Pin);
                        args.Accept();
                        break;

                    case DevicePairingKinds.ConfirmOnly:
                        args.Accept();
                        break;

                    case DevicePairingKinds.ProvidePin:
                        {
                            string pin = await GetPinFromUserAsync(dispatcher);
                            if (!String.IsNullOrEmpty(pin))
                            {
                                args.Accept(pin);
                            }
                        }
                        break;
                }
            }
        }

        
    }

    

    

    public class DiscoveredDevice : INotifyPropertyChanged
    {
        public DeviceInformation DeviceInfo { get; private set; }

        public DiscoveredDevice(DeviceInformation deviceInfo)
        {
            DeviceInfo = deviceInfo;
        }

        public string DisplayName => DeviceInfo.Name + " - " + (DeviceInfo.Pairing.IsPaired ? "已配对" : "未配对");
        public override string ToString() => DisplayName;

        public void UpdateDeviceInfo(DeviceInformationUpdate update)
        {
            DeviceInfo.Update(update);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("DisplayName"));
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}