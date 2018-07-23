using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;

namespace PhotoApp
{
    public sealed partial class MainPage : Page
    {
        public MainPage()
        {
            this.InitializeComponent();

            this.Loaded += OnLoaded;
        }
        async void OnLoaded(object sender, RoutedEventArgs e)
        {
            this.receiveSocket = new TcpListener(
                IPAddress.Loopback, 8888);

            this.receiveSocket.Start();

            // Accept one incoming connection and open up a stream over it for reading.
            using (var socket = await this.receiveSocket.AcceptSocketAsync())
            using (var receiveStream = new NetworkStream(socket))
            {
                var sizeBuffer = new byte[sizeof(ulong)];

                // We loop for as long as that connection works for us and we make no
                // attempt to recover if we have a read failure or similar.
                //
                // Note - because BitmapDecoder.CreateAsync() can take a stream without
                // having to specify a size, it seems like I can just feed the network
                // stream here straight into my function CreateSoftwareBitmapFromStreamAsync
                // but that won't work because BitmapDecoder wants a stream that it can
                // seek which the NetworkStream here won't be. Hence, we read the image
                // of the network into a MemoryStream and that means we need to know how
                // much we need to read and that means writing the size of the image down
                // the wire first.
                while (true)
                {
                    // Read the size of the incoming image from the stream.
                    var readCount = await receiveStream.ReadAsync(sizeBuffer, 0, sizeof(ulong));

                    if (readCount == sizeof(ulong))
                    {
                        // How big does the image say it is?
                        var imageSize = BitConverter.ToUInt64(sizeBuffer, 0);

                        // Make a memory stream that's big enough to store the incoming image.
                        using (var imageStream = new MemoryStream((int)imageSize))
                        {
                            // Copy from the network into that memory stream.
                            await RandomAccessStream.CopyAsync(
                                receiveStream.AsInputStream(),
                                imageStream.AsOutputStream(),
                                imageSize);

                            // Create an image source (this is a XAML thing) in order to be able
                            // to put the image on the screen.
                            var imageSource = await CreateSoftwareBitmapSourceFromStreamAsync(
                                imageStream.AsRandomAccessStream());

                            // We are not on the UI thread so need to switch to do this.
                            await this.Dispatcher.RunAsync(
                                CoreDispatcherPriority.Normal,
                                () =>
                                {
                                    // And put the image on the screen.
                                    ReplaceImageSoftwareBitmapSource(this.destImage, imageSource);
                                }
                            );
                        }
                    }
                }
            }
        }
        public async void OnSelectPhotoAsync()
        {
            // Raise a file dialog.
            var picker = new FileOpenPicker();
            picker.ViewMode = PickerViewMode.Thumbnail;
            picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".png");

            this.currentInputFile = await picker.PickSingleFileAsync();

            if (this.currentInputFile != null)
            {
                // Open the file.
                using (var stream = await this.currentInputFile.OpenReadAsync())
                {
                    // Create a SoftwareBitmapSource (a XAML thing)
                    var source = await CreateSoftwareBitmapSourceFromStreamAsync(stream);

                    // Replace that into our source image on the screen.
                    ReplaceImageSoftwareBitmapSource(this.srcImage, source);
                }
            }
        }
        static void ReplaceImageSoftwareBitmapSource(Image image, SoftwareBitmapSource newSource)
        {
            // Make sure we got rid of any previous image.
            if (image.Source != null)
            {
                ((SoftwareBitmapSource)image.Source).Dispose();
            }
            // And drop in the new one.
            image.Source = newSource;
        }
        static async Task<SoftwareBitmapSource> CreateSoftwareBitmapSourceFromStreamAsync(
            IRandomAccessStream stream)
        {
            // These are all XAML pieces involved with getting the image onto the screen.
            SoftwareBitmapSource source = null;

            var decoder = await BitmapDecoder.CreateAsync(stream);

            var bitmap = await decoder.GetSoftwareBitmapAsync(
                decoder.BitmapPixelFormat, BitmapAlphaMode.Premultiplied);

            source = new SoftwareBitmapSource();

            await source.SetBitmapAsync(bitmap);

            return (source);
        }
        public async void OnSendPhotoAsync()
        {
            // Make sure we have the socket created, connected and a stream ready to write to.
            await CreateSendSocketAndStreamAsync();

            if (this.currentInputFile != null)
            {
                // Open the file.
                using (var stream = await this.currentInputFile.OpenReadAsync())
                {
                    // Write out the size of the image stream as a ulong.
                    await this.sendStream.WriteAsync(BitConverter.GetBytes(stream.Size), 0, sizeof(ulong));

                    // Then write the entire stream to the network.
                    await RandomAccessStream.CopyAsync(stream, this.sendStream.AsOutputStream());
                }
            }
        }
        async Task CreateSendSocketAndStreamAsync()
        {
            if (this.sendSocket == null)
            {
                this.sendSocket = new TcpClient();

                await this.sendSocket.ConnectAsync(IPAddress.Loopback, 8888);

                this.sendStream = this.sendSocket.GetStream();
            }
        }
        NetworkStream sendStream;
        TcpClient sendSocket;
        TcpListener receiveSocket;
        StorageFile currentInputFile;
    }
}