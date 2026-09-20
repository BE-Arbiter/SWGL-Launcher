using NAudio.Wave;

namespace SWGLLauncher
{
    /// <summary>
    /// Lecture de la musique de fond (mp3, wav...) avec bouclage optionnel.
    /// Toute erreur de lecture est ignoree : le launcher doit s'ouvrir meme sans son.
    /// </summary>
    internal sealed class MusicPlayer : IDisposable
    {
        private WaveOutEvent? _output;
        private WaveStream? _reader;
        private LoopStream? _loop;
        private Stream? _source;

        /// <summary>Vrai si la musique est effectivement en cours de lecture.</summary>
        public bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing;

        /// <summary>
        /// Demarre la lecture d'un flux audio (fichier du disque ou ressource embarquee).
        /// Le flux est conserve jusqu'a l'arret et libere avec le lecteur.
        /// </summary>
        /// <param name="source">Flux audio, dont le lecteur prend la propriete.</param>
        /// <param name="extension">Extension d'origine, qui decide du decodeur.</param>
        /// <param name="loop">Rejouer en boucle a la fin du morceau.</param>
        /// <param name="volumePercent">Volume de 0 a 100.</param>
        public void Play(Stream source, string extension, bool loop, int volumePercent)
        {
            Stop();
            _source = source;

            try
            {
                _reader = CreateReader(source, extension);
                _output = new WaveOutEvent();

                if (loop)
                {
                    _loop = new LoopStream(_reader);
                    _output.Init(_loop);
                }
                else
                {
                    _output.Init(_reader);
                }

                _output.Volume = Math.Clamp(volumePercent, 0, 100) / 100f;
                _output.Play();
            }
            catch
            {
                // Peripherique audio absent, fichier corrompu, format non supporte...
                Stop();
            }
        }

        public void Stop()
        {
            try
            {
                _output?.Stop();
            }
            catch
            {
                // Ignore : on est probablement deja en train de fermer.
            }

            _output?.Dispose();
            _loop?.Dispose();
            _reader?.Dispose();
            _source?.Dispose();

            _output = null;
            _loop = null;
            _reader = null;
            _source = null;
        }

        private static WaveStream CreateReader(Stream source, string extension)
        {
            return extension.ToLowerInvariant() switch
            {
                ".mp3" => new Mp3FileReader(source),
                ".wav" => new WaveFileReader(source),
                _ => new StreamMediaFoundationReader(source),
            };
        }

        public void Dispose() => Stop();

        /// <summary>
        /// Enveloppe un flux audio pour le rejouer indefiniment.
        /// </summary>
        private sealed class LoopStream : WaveStream
        {
            private readonly WaveStream _source;

            public LoopStream(WaveStream source) => _source = source;

            public override WaveFormat WaveFormat => _source.WaveFormat;

            public override long Length => _source.Length;

            public override long Position
            {
                get => _source.Position;
                set => _source.Position = value;
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                int totalRead = 0;

                while (totalRead < count)
                {
                    int read = _source.Read(buffer, offset + totalRead, count - totalRead);

                    if (read == 0)
                    {
                        // Fin du morceau : on revient au debut.
                        if (_source.Position == 0)
                        {
                            break;
                        }

                        _source.Position = 0;
                    }

                    totalRead += read;
                }

                return totalRead;
            }
        }
    }
}
