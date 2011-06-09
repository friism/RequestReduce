﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace RequestReduce.Module
{
    public class ResponseFilter : AbstractFilter
    {
        private readonly Encoding encoding;
        private readonly IResponseTransformer responseTransformer;
        private byte[] StartStringUpper;
        private byte[] StartStringLower;
        private byte[] StartCloseChar;
        private byte[] EndStringUpper;
        private byte[] EndStringLower;
        private SearchState state = SearchState.LookForStart;
        private int matchPosition = 0;
        private List<byte> transformBuffer = new List<byte>();

        private enum SearchState
        {
            LookForStart,
            MatchingStart,
            MatchingStartClose,
            LookForStop,
            MatchingStop,
            MatchingFinished
        }

        private enum MatchingEnd
        {
            Start,
            End
        }

        public ResponseFilter(Stream baseStream, Encoding encoding, IResponseTransformer responseTransformer)
        {
            this.encoding = encoding;
            this.responseTransformer = responseTransformer;
            BaseStream = baseStream;

            StartStringUpper = encoding.GetBytes("<HEAD");
            StartStringLower = encoding.GetBytes("<head");
            StartCloseChar = encoding.GetBytes("> ");
            EndStringUpper = encoding.GetBytes("</HEAD>");
            EndStringLower = encoding.GetBytes("</head>");
        }

        protected Stream BaseStream { get; private set; }
        protected bool Closed { get; private set; }

        public override bool CanRead
        {
            get { return false; }
        }

        public override bool CanSeek
        {
            get { return false; }
        }

        public override bool CanWrite
        {
            get { return !Closed; }
        }

        public override void Close()
        {
            Closed = true;
            BaseStream.Close();
        }

        public override void Flush()
        {
            BaseStream.Flush();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (Closed) throw new ObjectDisposedException("ResponseFilter");
            if(state==SearchState.MatchingFinished)
            {
                BaseStream.Write(buffer, offset, count);
                return;
            }

            var startTransformPosition = 0;
            var endTransformPosition = 0;

            for (var idx = offset; idx < (count+offset); idx++)
                matchPosition = HandleMatch(idx, buffer[idx], ref startTransformPosition, ref endTransformPosition);

            switch (state)
            {
                case SearchState.LookForStart:
                case SearchState.MatchingStart:
                case SearchState.MatchingStartClose:
                    BaseStream.Write(buffer, offset, count);
                    return;
                case SearchState.LookForStop:
                case SearchState.MatchingStop:
                    BaseStream.Write(buffer, offset, startTransformPosition);
                    return;
                case SearchState.MatchingFinished:
                    if ((startTransformPosition - offset) >= 0)
                    BaseStream.Write(buffer, offset, startTransformPosition-offset);
                    var transformed = responseTransformer.Transform(encoding.GetString(transformBuffer.ToArray()));
                    BaseStream.Write(encoding.GetBytes(transformed), 0, transformed.Length);
                    if ((offset + count) - endTransformPosition > 0)
                        BaseStream.Write(buffer, endTransformPosition, (offset + count) - endTransformPosition);
                    return;
            }
        }

        private int HandleMatch(int i, byte b, ref int startTransformPosition, ref int endTransformPosition)
        {
            switch (state)
            {
                case SearchState.LookForStart:
                    return HandleLookForStartMatch(i, b);
                case SearchState.MatchingStart:
                    return HandleMatchingStartMatch(i, b);
                case SearchState.MatchingStartClose:
                    return HandleMatchingStartCloseMatch(i, b, ref startTransformPosition);
                case SearchState.LookForStop:
                    return HandleLookForStopMatch(i, b);
                case SearchState.MatchingStop:
                    return HandleMatchingStopMatch(i, b, ref endTransformPosition);
            }

            return matchPosition;
        }

        private int HandleMatchingStartCloseMatch(int i, byte b, ref int startTransformPosition)
        {
            if (StartCloseChar.Contains(b))
            {
                state = SearchState.LookForStop;
                startTransformPosition = ++i;
                return 0;
            }
            state = SearchState.LookForStart;
            return 0;
        }

        private int HandleMatchingStopMatch(int i, byte b, ref int endTransformPosition)
        {
            transformBuffer.Add(b);
            if (IsMatch(MatchingEnd.End, b))
            {
                matchPosition++;
                if(matchPosition==EndStringUpper.Length)
                {
                    endTransformPosition = ++i;
                    state = SearchState.MatchingFinished;
                    return 0;
                }
            }
            else
            {
                state = SearchState.LookForStop;
                return 0;
            }
            return matchPosition;
        }

        private int HandleLookForStopMatch(int i, byte b)
        {
            transformBuffer.Add(b);
            if(IsMatch(MatchingEnd.End, b))
            {
                state = SearchState.MatchingStop;
                matchPosition++;
            }
            return matchPosition;
        }

        private int HandleMatchingStartMatch(int i, byte b)
        {
            if(IsMatch(MatchingEnd.Start, b))
            {
                matchPosition++;
                if (matchPosition == StartStringUpper.Length)
                {
                    matchPosition = 0;
                    state = SearchState.MatchingStartClose;
                }
                return matchPosition;
            }
            state = SearchState.LookForStart;
            return 0;
        }

        private int HandleLookForStartMatch(int i, byte b)
        {
            if(IsMatch(MatchingEnd.Start, b))
            {
                state = SearchState.MatchingStart;
                transformBuffer.Clear();
                return HandleMatchingStartMatch(i, b);
            }
            return matchPosition;
        }

        private bool IsMatch(MatchingEnd end, byte b)
        {
            byte[] upper;
            byte[] lower;
            if(end == MatchingEnd.Start)
            {
                upper = StartStringUpper;
                lower = StartStringLower;
            }
            else
            {
                upper = EndStringUpper;
                lower = EndStringLower;
            }
            return b == lower[matchPosition] || b == upper[matchPosition];
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override long Length
        {
            get
            {
                throw new NotSupportedException();
            }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override long Position
        {
            get
            {
                throw new NotSupportedException();
            }
            set
            {
                throw new NotSupportedException();
            }
        }
    }
}