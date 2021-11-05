﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace tusdotnet.Tus2
{
    internal class OngoingUploadTransferServiceDiskBased
    {
        private readonly Tus2Options _options;
        private readonly Dictionary<string, Task> _cancelChecks;
        private readonly Dictionary<string, CancellationTokenSource> _finishFile;

        private const int FILE_CHECK_INTERVAL_IN_MS = 500;

        public OngoingUploadTransferServiceDiskBased(Tus2Options options)
        {
            _options = options;
            _cancelChecks = new Dictionary<string, Task>();
            _finishFile = new Dictionary<string, CancellationTokenSource>();
        }

        public Task<CancellationToken> StartOngoing(string uploadToken)
        {
            File.WriteAllBytes(_options.OngoingFilePath(uploadToken), Array.Empty<byte>());

            var cancelCts = new CancellationTokenSource();
            var finishCts = new CancellationTokenSource();

            var cancelTask = StartCheckingForCancelIndicationFile(uploadToken, cancelCts, finishCts);

            _cancelChecks.Add(uploadToken, cancelTask);
            _finishFile.Add(uploadToken, finishCts);
            return Task.FromResult(cancelCts.Token);
        }

        public Task FinishOngoing(string uploadToken)
        {
            File.Delete(_options.OngoingFilePath(uploadToken));

            var finishCts = _finishFile[uploadToken];
            finishCts.Cancel();
            finishCts.Dispose();
            
            _finishFile.Remove(uploadToken);
            _cancelChecks.Remove(uploadToken);

            return Task.CompletedTask;
        }

        public async Task CancelOngoingUploads(string uploadToken)
        {
            var ongoingFile = _options.OngoingFilePath(uploadToken);

            if (!File.Exists(ongoingFile))
                return;

            var cancelIndicationFile = _options.CancelFilePath(uploadToken);

            File.WriteAllBytes(cancelIndicationFile, Array.Empty<byte>());

            // Wait for indication file to be deleted but don't wait more than 30 seconds.
            const int TIMEOUT_IN_MS = 30_000;
            var elapsed = 0;

            do
            {
                await Task.Delay(500); // Chosen by fair dice roll
                elapsed += 500;

            } while (File.Exists(cancelIndicationFile) && elapsed < TIMEOUT_IN_MS);

            if (File.Exists(cancelIndicationFile))
                throw new TimeoutException("Timeout when trying to cancel other uploads");
        }

        private Task StartCheckingForCancelIndicationFile(string uploadToken, CancellationTokenSource cancelCts, CancellationTokenSource finishCts)
        {
            return Task.Run(async () =>
            {
                var cancelIndicationFile = _options.CancelFilePath(uploadToken);

                while (true)
                {
                    if (finishCts.IsCancellationRequested)
                        break;

                    await Task.Delay(FILE_CHECK_INTERVAL_IN_MS);

                    if (!File.Exists(cancelIndicationFile))
                        continue;

                    cancelCts.Cancel();
                    cancelCts.Dispose();
                    File.Delete(cancelIndicationFile);
                    break;
                }

            }, finishCts.Token);
        }
    }
}
