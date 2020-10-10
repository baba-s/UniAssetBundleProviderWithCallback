using Kogane.Internal;
using System;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.ResourceManagement.Util;
using AsyncOperation = UnityEngine.AsyncOperation;

namespace Kogane
{
	/// <summary>
	/// コールバック付きのアセットバンドルダウンロード処理
	/// </summary>
	public sealed class AssetBundleResourceWithCallback : IAssetBundleResource
	{
		//================================================================================
		// 変数
		//================================================================================
		private AssetBundle                 m_assetBundle;
		private DownloadHandlerAssetBundle  m_downloadHandler;
		private AsyncOperation              m_requestOperation;
		private WebRequestQueueOperation    m_webRequestQueueOperation;
		private ProvideHandle               m_provideHandle;
		private AssetBundleRequestOptions   m_options;
		private int                         m_retries;
		private long                        m_bytesToDownload;
		private JsonAssetBundleResourceData m_sendData;

		//================================================================================
		// イベント(static)
		//================================================================================
		/// <summary>
		/// 指定された URL が見つからない場合に呼び出されます
		/// </summary>
		public static event Action<JsonAssetBundleResourceData> OnUriFormatException;

		/// <summary>
		/// 読み込みを開始した時に呼び出されます
		/// </summary>
		public static event Action<JsonAssetBundleResourceData> OnStart;

		/// <summary>
		/// <para>読み込みが完了した時に呼び出されます</para>
		/// <para>読み込みに成功した場合も失敗した場合も呼び出されます</para>
		/// </summary>
		public static event Action<JsonAssetBundleResourceData> OnComplete;

		/// <summary>
		/// 読み込みに成功した時に呼び出されます
		/// </summary>
		public static event Action<JsonAssetBundleResourceData> OnSuccess;

		/// <summary>
		/// <para>読み込みに失敗してリトライする時に呼び出されます</para>
		/// <para>現在のリトライ回数とリトライの最大回数を渡します</para>
		/// </summary>
		public static event Action<JsonAssetBundleResourceData, int, int> OnRetry;

		/// <summary>
		/// <para>読み込みに失敗した場合に呼び出されます</para>
		/// <para>リトライする場合は、すべてのリトライに失敗した場合に呼び出されます</para>
		/// </summary>
		public static event Action<JsonAssetBundleResourceData, Action, Action> OnFailure;

		/// <summary>
		/// 読み込んだアセットバンドルがアンロードされた時に呼び出されます
		/// </summary>
		public static event Action<JsonAssetBundleResourceData> OnUnload;

		//================================================================================
		// 関数
		//================================================================================
		/// <summary>
		/// UnityWebRequest を作成して返します
		/// </summary>
		private UnityWebRequest CreateWebRequest( IResourceLocation location )
		{
			var url = m_provideHandle.ResourceManager.TransformInternalId( location );

			if ( m_options == null )
			{
				return UnityWebRequestAssetBundle.GetAssetBundle( url );
			}

			UnityWebRequest webRequest;

			if ( !string.IsNullOrEmpty( m_options.Hash ) )
			{
				var cachedBundle = new CachedAssetBundle
				(
					m_options.BundleName,
					Hash128.Parse( m_options.Hash )
				);

				if ( m_options.UseCrcForCachedBundle || !Caching.IsVersionCached( cachedBundle ) )
				{
					webRequest = UnityWebRequestAssetBundle.GetAssetBundle( url, cachedBundle, m_options.Crc );
				}
				else
				{
					webRequest = UnityWebRequestAssetBundle.GetAssetBundle( url, cachedBundle );
				}
			}
			else
			{
				webRequest = UnityWebRequestAssetBundle.GetAssetBundle( url, m_options.Crc );
			}

			if ( 0 < m_options.Timeout )
			{
				webRequest.timeout = m_options.Timeout;
			}

			if ( 0 < m_options.RedirectLimit )
			{
				webRequest.redirectLimit = m_options.RedirectLimit;
			}

#if !UNITY_2019_3_OR_NEWER
            webRequest.chunkedTransfer = m_Options.ChunkedTransfer;
#endif

			if ( m_provideHandle.ResourceManager.CertificateHandlerInstance == null )
			{
				return webRequest;
			}

			webRequest.certificateHandler                 = m_provideHandle.ResourceManager.CertificateHandlerInstance;
			webRequest.disposeCertificateHandlerOnDispose = false;

			return webRequest;
		}

		/// <summary>
		/// ダウンロードの進捗を返します
		/// </summary>
		private float PercentComplete()
		{
			return m_requestOperation?.progress ?? 0f;
		}

		/// <summary>
		/// ダウンロード状況を返します
		/// </summary>
		private DownloadStatus GetDownloadStatus()
		{
			if ( m_options == null ) return default;

			var status = new DownloadStatus
			{
				TotalBytes = m_bytesToDownload,
				IsDone     = 1 <= PercentComplete()
			};

			if ( m_bytesToDownload <= 0 )
			{
				return status;
			}

			if ( m_webRequestQueueOperation != null )
			{
				status.DownloadedBytes =
					( long ) m_webRequestQueueOperation.WebRequest.downloadedBytes;
			}
			else if ( 1 <= PercentComplete() )
			{
				status.DownloadedBytes = status.TotalBytes;
			}

			return status;
		}

		/// <summary>
		/// ダウンロードしたアセットバンドルを返します
		/// </summary>
		AssetBundle IAssetBundleResource.GetAssetBundle()
		{
			if ( m_assetBundle != null || m_downloadHandler == null )
			{
				return m_assetBundle;
			}

			m_assetBundle = m_downloadHandler.assetBundle;
			m_downloadHandler.Dispose();
			m_downloadHandler = null;

			return m_assetBundle;
		}

		/// <summary>
		/// ダウンロードを開始します
		/// </summary>
		internal void Start( ProvideHandle provideHandle )
		{
			m_sendData         = new JsonAssetBundleResourceData( provideHandle );
			m_retries          = 0;
			m_assetBundle      = null;
			m_downloadHandler  = null;
			m_requestOperation = null;
			m_provideHandle    = provideHandle;
			m_options          = ( AssetBundleRequestOptions ) m_provideHandle.Location.Data;

			if ( m_options != null )
			{
				m_bytesToDownload = m_options.ComputeSize
				(
					location: m_provideHandle.Location,
					resourceManager: m_provideHandle.ResourceManager
				);
			}

			provideHandle.SetProgressCallback( PercentComplete );
			provideHandle.SetDownloadProgressCallbacks( GetDownloadStatus );
			BeginOperation();
		}

		/// <summary>
		/// ダウンロードの処理を開始します
		/// </summary>
		private void BeginOperation()
		{
			try
			{
				OnStart?.Invoke( m_sendData );

				var path = m_provideHandle.ResourceManager.TransformInternalId( m_provideHandle.Location );

				if ( File.Exists( path ) || Application.platform == RuntimePlatform.Android && path.StartsWith( "jar:" ) )
				{
					m_requestOperation           =  AssetBundle.LoadFromFileAsync( path, m_options?.Crc ?? 0 );
					m_requestOperation.completed += LocalRequestOperationCompleted;
				}
				else if ( ResourceManagerConfig.ShouldPathUseWebRequest( path ) )
				{
					var req = CreateWebRequest( m_provideHandle.Location );

					req.disposeDownloadHandlerOnDispose = false;
					m_webRequestQueueOperation          = WebRequestQueue.QueueRequest( req );

					if ( m_webRequestQueueOperation.IsDone )
					{
						m_requestOperation           =  m_webRequestQueueOperation.Result;
						m_requestOperation.completed += WebRequestOperationCompleted;
					}
					else
					{
						m_webRequestQueueOperation.OnComplete += asyncOp =>
						{
							m_requestOperation           =  asyncOp;
							m_requestOperation.completed += WebRequestOperationCompleted;
						};
					}
				}
				else
				{
					m_requestOperation = null;
					m_provideHandle.Complete<AssetBundleResourceWithCallback>
					(
						result: null,
						status: false,
						exception: new Exception( $"Invalid path in AssetBundleProvider: '{path}'." )
					);
				}
			}
			catch ( UriFormatException )
			{
				OnUriFormatException?.Invoke( m_sendData );
			}
		}

		/// <summary>
		/// ローカルからのダウンロードが完了した時に呼び出されます
		/// </summary>
		private void LocalRequestOperationCompleted( AsyncOperation op )
		{
			m_assetBundle = ( ( AssetBundleCreateRequest ) op ).assetBundle;

			m_provideHandle.Complete
			(
				result: this,
				status: m_assetBundle != null,
				exception: null
			);
		}

		/// <summary>
		/// ダウンロードが完了した時呼び出されます
		/// </summary>
		private void WebRequestOperationCompleted( AsyncOperation op )
		{
			OnComplete?.Invoke( m_sendData );

			var remoteReq = ( UnityWebRequestAsyncOperation ) op;
			var webReq    = remoteReq.webRequest;

			m_downloadHandler = ( DownloadHandlerAssetBundle ) webReq.downloadHandler;

			if ( string.IsNullOrEmpty( webReq.error ) )
			{
				m_provideHandle.Complete
				(
					result: this,
					status: true,
					exception: null
				);

				OnSuccess?.Invoke( m_sendData );
			}
			else
			{
				m_downloadHandler.Dispose();
				m_downloadHandler = null;

				var forcedRetry = false;

				if ( !string.IsNullOrEmpty( m_options.Hash ) )
				{
					var cab = new CachedAssetBundle( m_options.BundleName, Hash128.Compute( m_options.Hash ) );

					if ( Caching.IsVersionCached( cab ) )
					{
						Caching.ClearCachedVersion( cab.name, cab.hash );

						if ( m_options.RetryCount == 0 && m_retries == 0 )
						{
							BeginOperation();
							m_retries++; //Will prevent us from entering an infinite loop of retrying if retry count is 0
							forcedRetry = true;
						}
					}
				}

				if ( !forcedRetry )
				{
					if ( m_retries < m_options.RetryCount )
					{
						OnRetry?.Invoke( m_sendData, m_retries, m_options.RetryCount );
						BeginOperation();
						m_retries++;
					}
					else
					{
						void OnRetry()
						{
							m_retries = 0;
							BeginOperation();
						}

						void OnComplete()
						{
							// ResourceManager.ExceptionHandler = null; しないと
							// この処理でエラーログが出力されてしまうので注意

							// NOTE: m_provideHandle.Complete　を呼び出すと必ず1回再通信が行われてしまう？要確認

							// m_provideHandle.Complete を呼び出さないと
							// 通信環境が悪くて通信に失敗した後に、通信環境が良い状態で
							// 再通信を行っても処理が何も進まなくなってしまうため
							// m_provideHandle.Complete は必ず呼び出す必要がある
							m_provideHandle.Complete<AssetBundleResourceWithCallback>
							(
								result: null,
								status: false,
								exception: null
							);
						}

						OnFailure?.Invoke
						(
							m_sendData,
							() => OnRetry(),
							() => OnComplete()
						);
					}
				}
			}

			webReq.Dispose();
		}

		/// <summary>
		/// ダウンロードしたアセットバンドルをアンロードします
		/// </summary>
		internal void Unload()
		{
			if ( m_assetBundle != null )
			{
				m_assetBundle.Unload( true );
				m_assetBundle = null;
			}

			if ( m_downloadHandler != null )
			{
				m_downloadHandler.Dispose();
				m_downloadHandler = null;
			}

			m_requestOperation = null;
			OnUnload?.Invoke( m_sendData );
		}

		//================================================================================
		// 関数(static)
		//================================================================================
		/// <summary>
		/// 最大同時通信数を設定します
		/// </summary>
		public static void SetMaxConcurrentRequests( int maxRequests )
		{
			WebRequestQueue.SetMaxConcurrentRequests( maxRequests );
		}
	}
}