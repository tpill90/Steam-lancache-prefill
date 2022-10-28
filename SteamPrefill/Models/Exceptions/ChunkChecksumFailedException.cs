namespace SteamPrefill.Models.Exceptions
{
    public class ChunkChecksumFailedException : Exception
    {
        public ChunkChecksumFailedException(string message) : base(message)
        {
        }

        public ChunkChecksumFailedException(string message, Exception innerException) : base(message, innerException)
        {
        }

        public ChunkChecksumFailedException()
        {
        }
    }
}
