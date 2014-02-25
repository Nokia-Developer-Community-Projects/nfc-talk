/*
 * Copyright © 2012 Nokia Corporation. All rights reserved.
 * Nokia and Nokia Connecting People are registered trademarks of Nokia Corporation. 
 * Other product and company names mentioned herein may be trademarks
 * or trade names of their respective owners. 
 * See LICENSE.TXT for license information.
 */
using ProtoBuf;
using System;
using System.IO;
using System.IO.IsolatedStorage;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Windows.Storage;

namespace NFCTalk
{
    /// <summary>
    /// Representation for a single chat message.
    /// </summary>
    [ProtoContract]
    public class Message
    {
        public static string PICTURES_FOLDER = "Pictures";
        public static System.Windows.Visibility VISIBLE = System.Windows.Visibility.Visible;
        public static System.Windows.Visibility COLLAPSED = System.Windows.Visibility.Collapsed;

        // Provides a quick way to access the full image file path
        public string ImagePath
        {
            get
            {
                return string.Concat(PICTURES_FOLDER, "\\", this.ImageName);
            }
        }

        // Returns a BitmapImage object of the image file in local storage for easy binding to the XAML
        public BitmapImage Image
        {
            get
            {
                BitmapImage image = new BitmapImage();

                using (IsolatedStorageFile isoStore = IsolatedStorageFile.GetUserStoreForApplication())
                {
                    if (isoStore.FileExists(ImagePath))
                    {
                        using (var stream = isoStore.OpenFile(ImagePath, System.IO.FileMode.Open))
                        {
                            image.SetSource(stream);
                        }
                    }
                }

                return image;
            }
        }

        // Handy non-async way of determining if an image file exists
        public bool ImageExists
        {
            get
            {
                bool imageExists = false;

                using (IsolatedStorageFile isoStore = IsolatedStorageFile.GetUserStoreForApplication())
                {
                    if (isoStore.FileExists(ImagePath))
                    {
                        imageExists = true;
                    }
                }

                return imageExists;
            }
        }

        // Property to easily bind to the image controls in XAML to determine when they should be visible or collapsed
        public System.Windows.Visibility ShowImage
        {
            get
            {
                return (ImageExists) ? VISIBLE : COLLAPSED;
            }
        }

        public enum DirectionValue
        {
            In = 0,
            Out = 1
        }

        /// <summary>
        /// Direction of message, in to this device, or out to the other device.
        /// </summary>
        [ProtoMember(1)]
        public DirectionValue Direction { get; set; }

        /// <summary>
        /// Sender's name.
        /// </summary>
        [ProtoMember(2)]
        public string Name { get; set; }

        /// <summary>
        /// Message.
        /// </summary>
        [ProtoMember(3)]
        public string Text { get; set; }

        /// <summary>
        /// Is this message archived.
        /// </summary>
        [ProtoMember(4)]
        public bool Archived { get; set; }

        [ProtoMember(5)]
        public string ImageName { get; set; }

        [ProtoMember(6)]
        public byte[] ImageBytes { get; set; }

        public async Task<bool> ImageExistsAsync(string overrideImageName = "")
        {
            bool exists = false;

            StorageFolder localFolder;
            StorageFolder pictureFolder;

            try
            {
                localFolder = Windows.Storage.ApplicationData.Current.LocalFolder;
                pictureFolder = await localFolder.CreateFolderAsync(PICTURES_FOLDER, CreationCollisionOption.OpenIfExists);

                exists = (File.Exists(string.Concat(pictureFolder.Path, "\\", overrideImageName.Equals("") ? this.ImageName : overrideImageName)));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(string.Concat("Failed to determine if the image exists. >>> ", ex));
            }
            finally
            {
                pictureFolder = null;
                localFolder = null;
                GC.Collect();
            }

            return exists;
        }

        public async Task<bool> ConvertImageToByteArrayAsync(string overrideImageName = "")
        {
            bool success = false;

            StorageFolder localFolder;
            StorageFolder pictureFolder;
            StorageFile tempFile;
            BitmapImage bi;
            WriteableBitmap wb;
            byte[] imageBuffer;

            try
            {
                if (!await ImageExistsAsync(overrideImageName))
                {
                    System.Diagnostics.Debug.WriteLine(string.Concat("Failed to create the Byte Array because there is no existing image."));
                }
                else
                {
                    // 1) Transform the image file into a JPEG byte array
                    localFolder = Windows.Storage.ApplicationData.Current.LocalFolder;
                    pictureFolder = await localFolder.CreateFolderAsync(PICTURES_FOLDER, CreationCollisionOption.OpenIfExists);
                    tempFile = await pictureFolder.GetFileAsync(overrideImageName.Equals("") ? this.ImageName : overrideImageName);
                    bi = new BitmapImage();
                    using (var s = await tempFile.OpenStreamForReadAsync())
                    {
                        bi.SetSource(s);
                    }
                    wb = new WriteableBitmap(bi);
                    using (var ms = new MemoryStream())
                    {
                        int quality = 90;
                        wb.SaveJpeg(ms, wb.PixelWidth, wb.PixelHeight, 0, quality);
                        imageBuffer = ms.ToArray();
                    }

                    // 2) Assign it to the message's byte array memeber and set success to true
                    this.ImageBytes = imageBuffer;
                    success = true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(string.Concat("Failed to create the Byte Array. >>> ", ex));
            }
            finally
            {
                imageBuffer = null;
                wb = null;
                bi = null;
                tempFile = null;
                pictureFolder = null;
                localFolder = null;
                GC.Collect();
            }

            return success;
        }

        public async Task<bool> SaveByteArrayImageToFileSystemAsync(bool cleanUpByteArray = true)
        {
            bool success = false;

            if (ImageBytes != null)
            {
                StorageFolder localFolder;
                StorageFolder pictureFolder;
                StorageFile tempFile;

                try
                {
                    // 1) Save the byte array to the local Pictures folder we created
                    localFolder = Windows.Storage.ApplicationData.Current.LocalFolder;
                    pictureFolder = await localFolder.CreateFolderAsync(PICTURES_FOLDER, CreationCollisionOption.OpenIfExists);
                    tempFile = await pictureFolder.CreateFileAsync(this.ImageName, CreationCollisionOption.ReplaceExisting);
                    using (var s = await tempFile.OpenStreamForWriteAsync())
                    {
                        s.Write(this.ImageBytes, 0, this.ImageBytes.Length);
                    }

                    // 2) Unless overridden, NULL out the byte array now that the image is safely on the file system.
                    if (cleanUpByteArray)
                    {
                        this.ImageBytes = null;
                    }
                    success = true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(string.Concat("Failed to save the image. >>> ", ex));
                }
                finally
                {
                    tempFile = null;
                    pictureFolder = null;
                    localFolder = null;
                    GC.Collect();
                }
            }

            return success;
        }
    }
}
