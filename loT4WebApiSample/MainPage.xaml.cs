﻿using loT4WebApiSample.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using System.Threading.Tasks;
using Windows.Storage;
using WebApiSample.Model;
using loT4WebApiSample.Common;
using loT4WebApiSample.Model;
using Windows.Web.Http;
using Sensors.Dht;
using Windows.Networking.PushNotifications;

//“空白页”项模板在 http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409 上有介绍

namespace loT4WebApiSample
{
    /// <summary>
    /// 可用于自身或导航至 Frame 内部的空白页。
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private WebCamHelper camera;
        private SpeechHelper speech;
        private GpioHelper gpioHelper;
        private FaceApiHelper faceApi;

        const string storageAccountName = "myblobsample";
        const string storageAccountKey = "JFyiGqG4Av5U7yc0RYIkvyHDW7taz5hl0TPdFLssZaUejbVKdkJvfhUEXNkfVU2usnEPzBc2MdzrCbvURHzrrQ==";
        const string containerName = "mycontainer";
        private BlobHelper blobHelper;

        //本地数据容器，重装可同步
        private StorageFolder localFolder = ApplicationData.Current.LocalFolder;
        //多平台同步容器
        private StorageFolder roamdingFolder = ApplicationData.Current.RoamingFolder;
        ApplicationDataContainer roamdingSettings = ApplicationData.Current.RoamingSettings;

        private DispatcherTimer timer_FaceRecognization;
        private DispatcherTimer timer_DhtSendValue;

        private bool isGpioValuable = false;
        private bool isDoorbellJustPress = false;

        private string userName = "cmn";
        private const string emergencePictureHost = "http://mywebapidemo.azurewebsites.net/api/EmergencyPicture";
        const string notificationHost = "http://mywebapidemo.azurewebsites.net/api/Command";
        //统计识别失败的次数，每5分钟重置一次
        private int faceRecognizationCount = 0;

        private PushNotificationChannel channel;
        const string CmdOn = "On";
        const string CmdOff = "Off";

        public MainPage()
        {
            this.InitializeComponent();

            faceApi = new FaceApiHelper();
            if(!isGpioValuable)
            {
                InitializeGpio();
            }

            InitializeTimer();
            InitializeBlobStorage();
            InitialCommand();
            InitialCommand();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            //启动Dht发送数据计时器，发送间隔见Constants
            if (timer_DhtSendValue != null)
                timer_DhtSendValue.Start();

            base.OnNavigatedTo(e);
        }

        public void InitializeTimer()
        {
            timer_FaceRecognization = new DispatcherTimer();
            timer_FaceRecognization.Interval = TimeSpan.FromMinutes(Constants.FaceConstants.FaceRecognizationFailedDuration);
            timer_FaceRecognization.Tick += Timer_FaceRecognization_Tick;

            timer_DhtSendValue = new DispatcherTimer();
            timer_DhtSendValue.Interval = TimeSpan.FromMinutes(Constants.GpioConstants.DhtSendValueDuration);
            timer_DhtSendValue.Tick += Timer_DhtSendValue_Tick;
        }

        private async void InitialCommand()
        {
            try
            {
                if (userName != string.Empty)
                {
                    PushNotificationChannel channel = 
                        await PushNotificationChannelManager.CreatePushNotificationChannelForApplicationAsync();

                    channel.PushNotificationReceived += Channel_PushNotificationReceived;

                    if (roamdingSettings.Values["channel"] == null
                        || roamdingSettings.Values["channel"].ToString() != channel.Uri)
                    {
                        CommandChannelInfo channelModel = new CommandChannelInfo();
                        channelModel.userName = userName;
                        channelModel.channelUri = channel.Uri;
                        channelModel.expirationTime = channel.ExpirationTime.DateTime;
                        string strJson = JsonHelper.ObjectToJson(channelModel);
                        HttpService httpService = new HttpService();
                        await httpService.SendPostRequest(notificationHost, strJson);
                        roamdingSettings.Values["channel"] = channel.Uri;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
        }

        private void Channel_PushNotificationReceived(PushNotificationChannel sender, PushNotificationReceivedEventArgs args)
        {
            RawNotification rawNotification = args.RawNotification;
            string commandContent = rawNotification.Content;
            if(commandContent==CmdOn&&isGpioValuable)
            {
                gpioHelper.OnTestLED();
            }
            else if(commandContent==CmdOff&&isGpioValuable)
            {
                gpioHelper.OffTestLED();
            }
        }

        private async void Timer_DhtSendValue_Tick(object sender, object e)
        {
            if (!isGpioValuable)
                return;

            DhtReading reader = new DhtReading();
            reader = await gpioHelper.GetDht().GetReadingAsync();//开始读取温湿度
            if(reader.IsValid)
            {
                double temperature = Convert.ToDouble(reader.Temperature);//读取温度
                double humidity = Convert.ToDouble(reader.Humidity);//读取湿度
            }
        }

        public void InitializeBlobStorage()
        {
            blobHelper = new BlobHelper();
            blobHelper.RunatAppStartUp(storageAccountName, storageAccountKey, containerName);
        }

        private void Timer_FaceRecognization_Tick(object sender, object e)
        {
            faceRecognizationCount = 0;
            timer_FaceRecognization.Stop();
        }

        public void InitializeGpio()
        {
            try
            {
                gpioHelper = new GpioHelper();
                isGpioValuable = gpioHelper.Initialize();
            }
            catch
            {
                isGpioValuable = false;
                Debug.WriteLine("GPIO controller不可用");
            }
            
            if(isGpioValuable)
            {
                gpioHelper.GetDoorBellPin().ValueChanged += doorbell_ValueChanged;
            }
        }

        private async void doorbell_ValueChanged(Windows.Devices.Gpio.GpioPin sender, Windows.Devices.Gpio.GpioPinValueChangedEventArgs args)
        {
            if(!isDoorbellJustPress)
            {
                if(args.Edge==Windows.Devices.Gpio.GpioPinEdge.FallingEdge)
                {
                    isDoorbellJustPress = true;
                    await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () => {
                        await BeginFaceRecognization();
                    });
                }
            }
        }

        private async Task BeginFaceRecognization()
        {
            speech.PlayTTS(Constants.SpeechConstants.GreetingMessage);
            //人脸识别
            if (camera.IsInitialized())
            {
                StorageFile imgFile = await camera.CapturePhoto();
                FaceInfo faceInfo = await faceApi.FaceDetection(imgFile);
                string faceListId = EncriptHelper.ToMd5(userName);
                string memberName = await faceApi.FaceSimilarWithMemberName(faceInfo.faceId,faceListId);
                if(string.Empty!=memberName)//识别成功，有权进入
                {
                    UnlockDoor();
                    speech.PlayTTS(Constants.SpeechConstants.GeneralGreetigMessage(memberName));
                }
                else
                {
                    if(faceRecognizationCount==0)
                    {
                        timer_FaceRecognization.Start();
                    }
                    ++faceRecognizationCount;
                    if(faceRecognizationCount>=Constants.FaceConstants.MaxFaceRecognizationFailed
                        &&blobHelper!=null)//在5分钟之内识别错误5次，将上传照片
                    {
                        await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
                        {
                            string imgName = camera.GenerateUserNameFileName(userName);
                            await blobHelper.uploadImage(imgName, imgFile);//发送图片到Blob Storage
                            await SendEmergencePictureUri(userName, GnerateEmergencePictureUri(imgName));//发送PictureUri到服务器
                        });
                    }
                    speech.PlayTTS(Constants.SpeechConstants.VisitorNotRecognizedMessage);
                }
            }

            isDoorbellJustPress = false;
        }

        private void UnlockDoor()
        {
            if(isGpioValuable)
            {
                gpioHelper.UnlockDoor();
            }
        }

        public string GnerateEmergencePictureUri(string imgName)
        {
            return "https://myblobsample.blob.core.windows.net/mycontainer/" + imgName;
        }

        private async Task SendEmergencePictureUri(string userName,string pictureUri)
        {
            HttpService httpService = new HttpService();
            EmergencePictureInfo emergencePicture = new EmergencePictureInfo();
            emergencePicture.userName = userName;
            emergencePicture.pictureUri = pictureUri;
            string jsonContent = JsonHelper.ObjectToJson(emergencePicture);
            HttpResponseMessage response = await httpService.SendPostRequest(emergencePictureHost, jsonContent);
            if(response.StatusCode!=HttpStatusCode.Ok)
            {
                Debug.WriteLine("EmergencePicture发送失败：" + response.Content);
            }
        }

        private void mediaElement_Loaded(object sender, RoutedEventArgs e)
        {
            if(speech==null)
            {
                speech = new SpeechHelper(mediaElement);
                speech.PlayTTS(Constants.SpeechConstants.InitialSpeechMessage);
            }
            else
            {
                mediaElement.AutoPlay = false;
            }
        }

        private async void cameraElement_Loaded(object sender, RoutedEventArgs e)
        {
            if(camera==null||!camera.IsInitialized())
            {
                camera = new WebCamHelper();
                await camera.InitializeCameraAsync();

                cameraElement.Source = camera.mediaCapture;

                if(cameraElement.Source!=null)
                {
                    await camera.StartCameraPreview();
                }
                else if(camera.IsInitialized())
                {
                    cameraElement.Source = camera.mediaCapture;
                    if(cameraElement.Source!=null)
                    {
                        await camera.StartCameraPreview();
                    }
                }
            }
        }
    }
}