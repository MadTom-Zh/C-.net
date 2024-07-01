using System;
using System.IO;

namespace MadTomDev.Data
{
    public class SECS : Stream
    {
        /// <summary>
        /// ��ʼ��������������ʵ��
        /// </summary>
        /// <param name="blockWidth">ÿ���ݿ�Ŀ�ȱ���Ϊ4�ı������߶�==��ȣ���֤������Ϊ���*3�����ԽС�ݴ�Խ�ã��������ʽ��ͣ�</param>
        /// <param name="bandWidth">һ�����ݴ������ݿ�����������Խ�࣬�����ɵ������������ݳ���Խ����</param>
        public SECS(Stream secStream, byte blockDataWidth = 3, int bandWidth = 512)
        {
            this.secStream = secStream;

            // basic checks
            if (!Block.CheckDataWidth(blockDataWidth, out Exception err))
                throw err;
            if (secStream == null)
                throw new NullReferenceException("The raw-stream must not be null.");
            if (bandWidth < 1)
                throw new ArgumentException("Band width must be at least 1.");

            // get basic index
            BlockDataWidth = blockDataWidth;
            BlockDataLength = blockDataWidth * blockDataWidth;
            BlockFullLength = (blockDataWidth + 3) * blockDataWidth;

            BandWidth = bandWidth;
            BandFullLength = BlockFullLength * bandWidth;
            BandDataLength = BlockDataLength * bandWidth;
            BandDataLength_noLengthData = BandDataLength - Band.lengthBytesLength;

            // load raw stream length
            loaderBand = new Band(blockDataWidth, bandWidth);
            if (CanSeek)
            {
                long totalBands_dec1 = secStream.Length / BandFullLength - 1;
                long tmp = totalBands_dec1 * BandDataLength_noLengthData;
                LoadBandFromSECS(totalBands_dec1);
                loaderBand.blocks[0].TryCorrecting(true);
                _Length = tmp + loaderBand.LoadLengthData();
            }
            else
            {
                _Length = 0;
            }
        }

        private Stream secStream;

        #region basic index
        public int BlockDataWidth { private set; get; }
        public int BlockDataLength { private set; get; }

        /// <summary>
        /// һ�����ݿ������(�ֽ�)���ȣ�����ԭ���ݺ;����룻
        /// </summary>
        public int BlockFullLength { private set; get; }
        /// <summary>
        /// һ�������������ٸ����ݿ飻
        /// </summary>
        public int BandWidth { private set; get; }

        public int BandFullLength { private set; get; }
        public int BandDataLength { private set; get; }
        public int BandDataLength_noLengthData { private set; get; }

        #endregion

        public override bool CanRead => secStream.CanRead;

        public override bool CanSeek => secStream.CanSeek;

        public override bool CanWrite => secStream.CanWrite;

        private long _Length = 0;
        public override long Length => _Length;
        public override void SetLength(long value)
        {
            _Length = value;
        }

        private long _Position = 0;
        public override long Position
        {
            get => _Position;
            set
            {
                if (_Position < 0)
                    throw new ArgumentOutOfRangeException("Position", "Position must be greator than 0.");
                if (_Position > _Length)
                    throw new ArgumentOutOfRangeException("Position", "Position must be within Length");
                _Position = value;
            }
        }


        public override long Seek(long offset, SeekOrigin origin)
        {
            switch (origin)
            {
                case SeekOrigin.Current:
                    Position += offset;
                    break;
                case SeekOrigin.Begin:
                    Position = offset;
                    break;
                case SeekOrigin.End:
                    Position = _Length + offset;
                    break;
            }
            return _Position;
        }

        private long loaderBandIndex = -1;
        private Band loaderBand;
        private bool LoadBandFromSECS(long startBand, bool forceReload = false)
        {
            if (loaderBandIndex == startBand && !forceReload)
                return true;

            long iPosi = startBand * BandFullLength;
            if (iPosi >= secStream.Length)
                return false;
            secStream.Position = iPosi;
            int readLength = secStream.Read(loaderBand.bandBuffer, 0, BandFullLength);
            if (readLength != BandFullLength)
                throw new DataMisalignedException("Read data insufficient.");
            loaderBand.LoadBlocksFromBandBuffer();
            //loaderBand.TryCurrect();
            loaderBandIndex = startBand;
            return true;
        }

        /// <summary>
        /// �Ӿ������л�ȡ(������)ԭʼ����
        /// </summary>
        /// <param name="buffer">������������ݵ�����</param>
        /// <param name="offset">��buffer��offsetλ�ÿ�ʼд�����������</param>
        /// <param name="count">Ҫ��������ݵĳ���</param>
        /// <returns>ʵ�ʶ������ݵĳ���</returns>
        public override int Read(byte[] buffer, int offset, int count)
        {
            long bandIdx = _Position / BandDataLength_noLengthData;
            int bandLength = (count + BandDataLength_noLengthData - 1) / BandDataLength_noLengthData;
            int i = (int)(_Position % BandDataLength_noLengthData);
            int iv;

            int iCount = count;
            for (int bi = 0; bi < bandLength; bi++)
            {
                if (!LoadBandFromSECS(bandIdx + bi))
                    break;
                if (!loaderBand.TryCurrect())
                    throw new Exception($"Data cruption at band [{bi}].");
                loaderBand.LoadBandDataFromBlocks();
                for (iv = loaderBand.LoadLengthData(); i < iv; i++)
                {
                    buffer[offset++] = loaderBand.bandData[i];
                    iCount--;
                    if (iCount <= 0)
                        break;
                }
                i = 0;
            }
            int readCount = count - iCount;
            _Position += readCount;
            return readCount;
        }



        /// <summary>
        /// ��ԭʼ������д�뵽������(������)
        /// </summary>
        /// <param name="buffer">������д�����ݵ���Դ</param>
        /// <param name="offset">�ӻ�����ĸ�λ�ÿ�ʼ��ȡҪд�������</param>
        /// <param name="count">Ҫд��ĳ���</param>
        public override void Write(byte[] buffer, int offset, int count)
        {
            long bandIdx = _Position / BandDataLength_noLengthData;
            int bandLength = (count + BandDataLength_noLengthData) / BandDataLength_noLengthData;

            int i = (int)(_Position % BandDataLength_noLengthData);
            int writeLen = Math.Min(count, BandDataLength_noLengthData - i);
            long secBandStart;
            for (int bi = 0; bi < bandLength; bi++)
            {
                secBandStart = (bandIdx + bi) * BandFullLength;
                if (secBandStart >= secStream.Length)
                {
                    // add new band
                    loaderBandIndex = bandIdx + bi;
                    loaderBand.Clear();
                }
                else
                {
                    // to exist band
                    LoadBandFromSECS(bandIdx + bi);
                    if (!loaderBand.TryCurrect())
                        throw new Exception($"Data cruption at band [{bi}].");
                }
                loaderBand.Position = i;
                loaderBand.Write(buffer, offset, writeLen);
                loaderBand.WriteLength();
                loaderBand.GenerateCCs();
                loaderBand.FlushToBandBuffer();

                secStream.Position = secBandStart;
                secStream.Write(loaderBand.bandBuffer, 0, BandFullLength);

                _Position += writeLen;
                offset += writeLen;
                if (_Length < _Position)
                    _Length = _Position;

                i = 0;
                count -= writeLen;
                if (count <= 0)
                    break;
                writeLen = Math.Min(count, BandDataLength_noLengthData);
            }
        }
        public override void Flush()
        {
            secStream.Flush();
        }

        public void CopyTo_optimized(Stream targetStream)
        {
            int bufferLen = BandDataLength_noLengthData - (int)(Position % BandDataLength_noLengthData);
            byte[] buffer = new byte[BandDataLength_noLengthData];
            int readLength;
            do
            {
                readLength = Read(buffer, 0, bufferLen);
                targetStream.Write(buffer, 0, readLength);
                bufferLen = BandDataLength_noLengthData;
            }
            while (readLength > 0);
        }
        public void CopyTo_optimized(SECS targetStream)
        {
            int sourceBufferLen = BandDataLength_noLengthData - (int)(Position % BandDataLength_noLengthData);
            byte[] buffer = new byte[this.BandDataLength_noLengthData + targetStream.BandDataLength_noLengthData];
            int writeStart = 0, offset = 0;

            int targetBufferLen = targetStream.BandDataLength_noLengthData - (int)(targetStream.Position % targetStream.BandDataLength_noLengthData);

            int readLength, i, j;
            bool isInitWrite = true, isInitRead = true;
            while (true)
            {
                readLength = Read(buffer, offset, sourceBufferLen);
                if (readLength <= 0)
                    break;

                offset += readLength;
                while ((offset - writeStart) >= targetBufferLen)
                {
                    // �����������㹻дĿ������ݳ��Ⱥ󣬿�ʼ��Ŀ��д��
                    // ���ǰ���ζ�ȡ�ĳ��Ȳ�������ִ��������룬����ѭ����ȡ��
                    targetStream.Write(buffer, writeStart, targetBufferLen);
                    writeStart += targetBufferLen;
                    if (isInitWrite)
                    {
                        targetBufferLen = targetStream.BandDataLength_noLengthData;
                        isInitWrite = false;
                    }
                }
                if (writeStart > 0)
                {
                    // ��������û��д������ݣ�ת�Ƶ���ǰ��
                    for (i = 0, j = writeStart; j < offset; i++, j++)
                    {
                        buffer[i] = buffer[j];
                    }
                    writeStart = 0;
                    offset = i;
                }

                if (isInitRead)
                {
                    sourceBufferLen = BandDataLength_noLengthData;
                }
            }

            if (offset > 0)
            {
                targetStream.Write(buffer, 0, offset);
            }
        }
        public void CopyFrom_optimized(Stream sourceStream)
        {
            byte[] buffer = new byte[BandDataLength_noLengthData ];
            int bufferLen = BandDataLength_noLengthData - (int)(Position % BandDataLength_noLengthData);
            int readLength;
            bool isInit = true;
            while (true)
            {
                readLength = sourceStream.Read(buffer, 0, bufferLen);
                if (readLength <= 0)
                    break;
                Write(buffer, 0, readLength);
                if (isInit)
                {
                    bufferLen = BandDataLength_noLengthData;
                    isInit = false;
                }
            }
        }
        public void CopyFrom_optimized(SECS sourceStream)
        {
            sourceStream.CopyTo_optimized(this);
        }
    }

}
