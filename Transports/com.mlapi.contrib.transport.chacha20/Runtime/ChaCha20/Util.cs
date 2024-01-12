/*
 * Copyright (c) 2015, 2018 Scott Bennett
 *
 * Permission to use, copy, modify, and distribute this software for any
 * purpose with or without fee is hereby granted, provided that the above
 * copyright notice and this permission notice appear in all copies.
 *
 * THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES
 * WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
 * MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR
 * ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
 * WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN
 * ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF
 * OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.
 */

// Modified by Albin Corén, https://github.com/twotenpvp (@Unity Technologies)

using System;

namespace MLAPI.Transport.ChaCha20.ChaCha20
{
    public class Util
    {
        /// <summary>
        /// n-bit left rotation operation (towards the high bits) for 32-bit
        /// integers.
        /// </summary>
        /// <param name="v"></param>
        /// <param name="c"></param>
        /// <returns>The result of (v LEFTSHIFT c)</returns>
        public static uint Rotate(uint v, int c)
        {
            unchecked
            {
                return (v << c) | (v >> (32 - c));
            }
        }

        /// <summary>
        /// Unchecked integer exclusive or (XOR) operation.
        /// </summary>
        /// <param name="v"></param>
        /// <param name="w"></param>
        /// <returns>The result of (v XOR w)</returns>
        public static uint XOr(uint v, uint w)
        {
            return unchecked(v ^ w);
        }

        /// <summary>
        /// Unchecked integer addition. The ChaCha spec defines certain operations
        /// to use 32-bit unsigned integer addition modulo 2^32.
        /// </summary>
        /// <remarks>
        /// <remarks>
        /// See <a href="https://tools.ietf.org/html/rfc7539#page-4">ChaCha20 Spec Section 2.1</a>.
        /// </remarks>
        /// </remarks>
        /// <param name="v"></param>
        /// <param name="w"></param>
        /// <returns>The result of (v + w) modulo 2^32</returns>
        public static uint Add(uint v, uint w)
        {
            return unchecked(v + w);
        }

        /// <summary>
        /// Add 1 to the input parameter using unchecked integer addition. The
        /// ChaCha spec defines certain operations to use 32-bit unsigned integer
        /// addition modulo 2^32.
        /// </summary>
        /// <remarks>
        /// See <a href="https://tools.ietf.org/html/rfc7539#page-4">ChaCha20 Spec Section 2.1</a>.
        /// </remarks>
        /// <param name="v"></param>
        /// <returns>The result of (v + 1) modulo 2^32</returns>
        public static uint AddOne(uint v)
        {
            return unchecked(v + 1);
        }

        /// <summary>
        /// Convert four bytes of the input buffer into an unsigned
        /// 32-bit integer, beginning at the inputOffset.
        /// </summary>
        /// <param name="p"></param>
        /// <param name="inputOffset"></param>
        /// <returns>An unsigned 32-bit integer</returns>
        public static uint U8To32Little(byte[] p, int inputOffset)
        {
            unchecked
            {
                return ((uint) p[inputOffset]
                        | ((uint) p[inputOffset + 1] << 8)
                        | ((uint) p[inputOffset + 2] << 16)
                        | ((uint) p[inputOffset + 3] << 24));
            }
        }

        /// <summary>
        /// Serialize the input integer into the output buffer. The input integer
        /// will be split into 4 bytes and put into four sequential places in the
        /// output buffer, starting at the outputOffset.
        /// </summary>
        /// <param name="output"></param>
        /// <param name="input"></param>
        /// <param name="outputOffset"></param>
        public static void ToBytes(byte[] output, uint input, int outputOffset)
        {
            if (outputOffset < 0)
            {
                throw new ArgumentOutOfRangeException("outputOffset", "The buffer offset cannot be negative");
            }

            unchecked
            {
                output[outputOffset] = (byte) input;
                output[outputOffset + 1] = (byte) (input >> 8);
                output[outputOffset + 2] = (byte) (input >> 16);
                output[outputOffset + 3] = (byte) (input >> 24);
            }
        }
    }
}
