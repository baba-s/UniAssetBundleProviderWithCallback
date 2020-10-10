using System;
using UnityEngine.Networking;

namespace Kogane.Internal
{
	internal sealed class WebRequestQueueOperation
	{
		internal UnityWebRequestAsyncOperation         Result     { get; private set; }
		internal Action<UnityWebRequestAsyncOperation> OnComplete { get; set; }
		internal UnityWebRequest                       WebRequest { get; }

		internal bool IsDone => Result != null;

		internal WebRequestQueueOperation( UnityWebRequest request )
		{
			WebRequest = request;
		}

		internal void Complete( UnityWebRequestAsyncOperation asyncOp )
		{
			Result = asyncOp;
			OnComplete?.Invoke( Result );
		}
	}
}