// <copyright file="DeployType.cs">
// The MIT License
// Copyright © Christopher McNeely
// Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
// The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
// </copyright>

namespace AvConsoleToolkit.CertManager
{
    /// <summary>
    /// Specifies the deployment method used to upload certificates to a remote device.
    /// </summary>
    public enum DeployType
    {
        /// <summary>
        /// Crestron Series 3 processor deployment via SSH.
        /// </summary>
        Crestron3,

        /// <summary>
        /// Crestron Series 4 processor deployment via SSH.
        /// </summary>
        Crestron4,

        /// <summary>
        /// Crestron 60 Series touchpanel deployment via SSH.
        /// </summary>
        CrestronTP60Series,

        /// <summary>
        /// Crestron 70 Series touchpanel deployment via SSH.
        /// </summary>
        CrestronTP70Series,

        /// <summary>
        /// TrueNAS Scale server deployment via REST API.
        /// </summary>
        TrueNas,

        /// <summary>
        /// UniFi device deployment via SSH (e.g., Dream Machine Pro).
        /// </summary>
        UniFi,

        /// <summary>
        /// Generic SCP deployment to a specified directory on the target device.
        /// </summary>
        Scp,
    }
}
