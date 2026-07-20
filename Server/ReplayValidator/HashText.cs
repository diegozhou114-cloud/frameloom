namespace ZWebGLFrameWork.Server
{
    public static class HashText
    {
        public static bool IsSha256(string value)
        {
            if (value == null || value.Length != 64) return false;
            for (int index = 0; index < value.Length; index++)
            {
                char current = value[index];
                if (!((current >= '0' && current <= '9') ||
                      (current >= 'a' && current <= 'f')))
                    return false;
            }
            return true;
        }

        public static string ToLowerHex(byte[] bytes)
        {
            var text = new char[bytes.Length * 2];
            const string alphabet = "0123456789abcdef";
            for (int index = 0; index < bytes.Length; index++)
            {
                text[index * 2] = alphabet[bytes[index] >> 4];
                text[index * 2 + 1] = alphabet[bytes[index] & 15];
            }
            return new string(text);
        }

        public static bool FixedTimeEquals(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
                return false;
            int difference = 0;
            for (int index = 0; index < left.Length; index++)
                difference |= left[index] ^ right[index];
            return difference == 0;
        }
    }
}
