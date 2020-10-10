using JetBrains.Annotations;
using System;
using System.ComponentModel;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;

namespace Kogane
{
	/// <summary>
	/// <para>コールバック付きのアセットバンドルダウンロード処理を使用するためのクラス</para>
	/// <para>BundledAssetGroupSchema の Inspector で設定するために用意しています</para>
	/// </summary>
	[DisplayName( "AssetBundle Provider With Callback" )]
	[UsedImplicitly]
	public sealed class AssetBundleProviderWithCallback : ResourceProviderBase
	{
		//================================================================================
		// 関数
		//================================================================================
		/// <summary>
		/// アセットバンドルのダウンロードを開始する時に呼び出されます
		/// </summary>
		public override void Provide( ProvideHandle providerInterface )
		{
			var resource = new AssetBundleResourceWithCallback();
			resource.Start( providerInterface );
		}

		/// <summary>
		/// デフォルトの型を返します
		/// </summary>
		public override Type GetDefaultType( IResourceLocation location )
		{
			return typeof( IAssetBundleResource );
		}

		/// <summary>
		/// アセットバンドルを解放する時に呼び出されます
		/// </summary>
		public override void Release( IResourceLocation location, object asset )
		{
			var resource = ( AssetBundleResourceWithCallback ) asset;
			resource.Unload();
		}
	}
}