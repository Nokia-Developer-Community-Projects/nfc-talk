/*
 * Copyright © 2012-2013 Nokia Corporation. All rights reserved.
 * Nokia and Nokia Connecting People are registered trademarks of Nokia Corporation. 
 * Other product and company names mentioned herein may be trademarks
 * or trade names of their respective owners. 
 * See LICENSE.TXT for license information.
 */

using Microsoft.Phone.Controls;
using Microsoft.Phone.Shell;
using Microsoft.Phone.Tasks;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Windows.Storage;

namespace NFCTalk
{
    /// <summary>
    /// TalkPage displays the currently active chat session and also
    /// grayed out messages from previous chat sessions.
    /// </summary>
    public partial class TalkPage : PhoneApplicationPage
    {
        private NFCTalk.DataContext _dataContext = NFCTalk.DataContext.Singleton;
        private ApplicationBarIconButton sendButton = new ApplicationBarIconButton();
        private PhotoChooserTask m_PhotoChooserTask = new PhotoChooserTask();
        private string m_TempImageName = "tempImage.jpg";
        private bool m_ImageReadyToTransfer = false;

        void scrollToLast()
        {
            if (talkListBox.Items.Count > 0)
            {
                talkListBox.UpdateLayout();
                talkListBox.ScrollIntoView(talkListBox.Items[talkListBox.Items.Count - 1]);
            }
        }

        public TalkPage()
        {
            InitializeComponent();

            m_PhotoChooserTask.Completed += PhotoChooserTask_Completed;
            BuildApplicationBar();

            DataContext = _dataContext;
        }

        private void BuildApplicationBar()
        {
            // Set the page's ApplicationBar to a new instance of ApplicationBar.
            ApplicationBar = new ApplicationBar();

            sendButton = new ApplicationBarIconButton(new Uri("/Assets/Icons/appbar.message.send.png", UriKind.Relative));
            sendButton.Text = "Send";
            sendButton.Click += sendButton_Click;
            ApplicationBar.Buttons.Add(sendButton);

            ApplicationBarIconButton addImageButton = new ApplicationBarIconButton(new Uri("/Assets/Icons/appbar.image.png", UriKind.Relative));
            addImageButton.Text = "Add Image";
            addImageButton.Click += addImageButton_Click;
            ApplicationBar.Buttons.Add(addImageButton);
        }

        /// <summary>
        /// Chat message list is scrolled to the last message sent or received and listening to
        /// incoming messages is started.
        /// </summary>
        /// <param name="e"></param>
        protected override void OnNavigatedTo(System.Windows.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (!App.PhotoActivity)
            {
                if (_dataContext.Communication.IsConnected)
                {
                    sendButton.IsEnabled = false;

                    _dataContext.Communication.ConnectionInterrupted += ConnectionInterrupted;
                    _dataContext.Communication.MessageReceived += MessageReceived;

                    _dataContext.Messages.CollectionChanged += MessagesChanged;

                    Deployment.Current.Dispatcher.BeginInvoke(() =>
                    {
                        scrollToLast();
                    });
                }
                else
                {
                    NavigationService.GoBack();
                }
            }
        }

        /// <summary>
        /// Leaving TalkPage causes the current chat session to be disconnected and all
        /// stored messages to be marked as archived.
        /// </summary>
        protected override void OnNavigatingFrom(System.Windows.Navigation.NavigatingCancelEventArgs e)
        {
            base.OnNavigatingFrom(e);

            if (!App.PhotoActivity)
            {
                _dataContext.Communication.ConnectionInterrupted -= ConnectionInterrupted;
                _dataContext.Communication.MessageReceived -= MessageReceived;

                _dataContext.Messages.CollectionChanged -= MessagesChanged;

                _dataContext.Communication.Disconnect();

                foreach (Message m in _dataContext.Messages)
                {
                    m.Archived = true;
                }
            }
        }

        /// <summary>
        /// Event handler to be executed when messages change.
        /// 
        /// TalkPage message list is scrolled to the last message.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MessagesChanged(object sender, EventArgs e)
        {
            scrollToLast();
        }

        /// <summary>
        /// Event handler to be executed when a new inbound message has been received.
        /// 
        /// Message is stored to the DataContext.Messages.
        /// </summary>
        /// <param name="m"></param>
        private void MessageReceived(Message m)
        {
            Deployment.Current.Dispatcher.BeginInvoke(async () =>
            {
                await m.SaveByteArrayImageToFileSystemAsync();

                _dataContext.Messages.Add(m);
            });
        }

        /// <summary>
        /// Event handler to be executed when connection is interrupted.
        /// 
        /// Chat session is disconnected and application navigates back to the MainPage.
        /// </summary>
        private void ConnectionInterrupted()
        {
            Dispatcher.BeginInvoke(() =>
                {
                    NavigationService.GoBack();
                });
        }

        /// <summary>
        /// Event handler to be executed when send button is clicked.
        /// 
        /// New outbound message is constructed using the configured chat name and
        /// chat message from the message input field. Message is send to the other device.
        /// </summary>
        private async void sendButton_Click(object sender, EventArgs e)
        {
            Message m = new Message()
            {
                Name = _dataContext.Settings.Name,
                Text = messageInput.Text,
                Direction = Message.DirectionValue.Out
            };

            // If we are transferring an image, load it into the message from the temporary image file
            if (m_ImageReadyToTransfer)
            {
                m.ImageName = string.Format("{0}.jpg", Guid.NewGuid()); // assign a unique image name
                await m.ConvertImageToByteArrayAsync(m_TempImageName); // load up the temporary image into the message's byte array
                await m.SaveByteArrayImageToFileSystemAsync(false); // save a local copy of the image, and do not clean up the byte array
            }

            // Clean up input values
            messageInput.Text = "";
            messageImage.Source = null;
            messageImage.Visibility = System.Windows.Visibility.Collapsed;
            m_ImageReadyToTransfer = false;

            _dataContext.Messages.Add(m);

            await _dataContext.Communication.SendMessageAsync(m);

            scrollToLast();
        }

        /// <summary>
        /// Event handler to be executed when message input field content changes.
        /// 
        /// Send button is enabled if message exists, otherwise send button is disabled.
        /// </summary>
        private void messageInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            sendButton.IsEnabled = (m_ImageReadyToTransfer || messageInput.Text.Length > 0);
        }

        private void addImageButton_Click(object sender, EventArgs e)
        {
            App.PhotoActivity = true;
            m_PhotoChooserTask.Show();
        }

        private async void PhotoChooserTask_Completed(object sender, PhotoResult e)
        {
            if (e.TaskResult == TaskResult.OK)
            {
                StorageFolder localFolder;
                StorageFolder pictureFolder;
                StorageFile tempFile;
                BitmapImage bi;
                WriteableBitmap wb;

                try
                {
                    // 1) Assign the selected image to the messageImage control
                    bi = new BitmapImage();
                    bi.SetSource(e.ChosenPhoto);
                    messageImage.Source = bi;
                    messageImage.Visibility = System.Windows.Visibility.Visible;

                    // 2) Write it to local storage
                    localFolder = Windows.Storage.ApplicationData.Current.LocalFolder;
                    pictureFolder = await localFolder.CreateFolderAsync(Message.PICTURES_FOLDER, CreationCollisionOption.OpenIfExists);
                    tempFile = await pictureFolder.CreateFileAsync(m_TempImageName, Windows.Storage.CreationCollisionOption.ReplaceExisting);
                    using (var wfs = await tempFile.OpenStreamForWriteAsync())
                    {
                        e.ChosenPhoto.Seek(0, SeekOrigin.Begin);
                        await e.ChosenPhoto.CopyToAsync(wfs);
                    }
                    m_ImageReadyToTransfer = true;
                    sendButton.IsEnabled = (m_ImageReadyToTransfer || messageInput.Text.Length > 0);

                    System.Diagnostics.Debug.WriteLine(string.Concat("Successfully saved the image: ", tempFile.Path));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(string.Concat("Failed to save the image >>> ", ex));
                }
                finally
                {
                    wb = null;
                    bi = null;
                    tempFile = null;
                    pictureFolder = null;
                    localFolder = null;
                    GC.Collect();
                }
            }

            App.PhotoActivity = false;
        }


    }
}