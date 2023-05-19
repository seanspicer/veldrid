// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;

namespace Veldrid.MTL
{
    internal interface IMTLDisplayLink : IDisposable
    {
        event Action Callback;
    }
}