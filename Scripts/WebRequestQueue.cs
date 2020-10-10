using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace Kogane.Internal
{
	internal static class WebRequestQueue
	{
		private static int                                 m_maxRequest       = 10;
		private static Queue<WebRequestQueueOperation>     m_queuedOperations = new Queue<WebRequestQueueOperation>();
		private static List<UnityWebRequestAsyncOperation> m_activeRequests   = new List<UnityWebRequestAsyncOperation>();

		internal static void SetMaxConcurrentRequests( int maxRequests )
		{
			if ( maxRequests < 1 )
			{
				throw new ArgumentException( "MaxRequests must be 1 or greater.", "maxRequests" );
			}

			m_maxRequest = maxRequests;
		}

		internal static WebRequestQueueOperation QueueRequest( UnityWebRequest request )
		{
			var queueOperation = new WebRequestQueueOperation( request );

			if ( m_activeRequests.Count < m_maxRequest )
			{
				var webRequestAsyncOp = request.SendWebRequest();
				webRequestAsyncOp.completed += OnWebAsyncOpComplete;
				m_activeRequests.Add( webRequestAsyncOp );
				queueOperation.Complete( webRequestAsyncOp );
			}
			else
			{
				m_queuedOperations.Enqueue( queueOperation );
			}

			return queueOperation;
		}

		private static void OnWebAsyncOpComplete( AsyncOperation operation )
		{
			m_activeRequests.Remove( operation as UnityWebRequestAsyncOperation );

			if ( m_queuedOperations.Count <= 0 ) return;

			var nextQueuedOperation = m_queuedOperations.Dequeue();
			var webRequestAsyncOp   = nextQueuedOperation.WebRequest.SendWebRequest();

			webRequestAsyncOp.completed += OnWebAsyncOpComplete;
			m_activeRequests.Add( webRequestAsyncOp );
			nextQueuedOperation.Complete( webRequestAsyncOp );
		}
	}
}