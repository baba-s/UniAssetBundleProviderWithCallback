# UniAssetBundleProviderWithCallback

Addressable の AssetBundleProvider にコールバック機能を追加したパッケージ

## 使い方

![2020-10-11_143130](https://user-images.githubusercontent.com/6134875/95671168-8fe12e80-0bce-11eb-9885-bedb1d7d16f2.png)

Addressable Asset Group の AssetBundle Provider を  
「AssetBundle Provider With Callback」にしてアセットバンドルをビルドすることで  
アセットバンドルのダウンロード処理でこのパッケージの Provider が使用されるようになります  

そして、以下のようなコードを記述することで  
アセットバンドルのダウンロードの開始時や終了時などにコールバックが呼び出されるようになります  

```cs
using Kogane;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

public class Example : MonoBehaviour
{
    // ダウンロード中のアセットバンドルのリスト
    private readonly List<(JsonAssetBundleResourceData, bool)> m_downloadingList 
        = new List<(JsonAssetBundleResourceData, bool)>();

    private void Start()
    {
        // 指定された URL が見つからない場合に呼び出されます
        AssetBundleResourceWithCallback.OnUriFormatException += data =>
        {
            Debug.LogError( $"onUriFormatException: {data.Url}" );
        };

        // 読み込みを開始した時に呼び出されます
        AssetBundleResourceWithCallback.OnStart += data =>
        {
            m_downloadingList.Add( ( data, data.IsCached ) );

            var isCached = data.IsCached;
            var sb       = new StringBuilder();

            sb.Append( isCached ? "キャッシュからロード開始" : "サーバからダウンロード開始" );
            sb.Append( "：" );
            sb.AppendLine( Path.GetFileName( data.Url ) );
            sb.AppendLine( data.ToPrettyJson() );

            Debug.Log( sb.ToString() );
        };

        // 読み込みに成功した時に呼び出されます
        AssetBundleResourceWithCallback.OnSuccess += data =>
        {
            var index        = m_downloadingList.FindIndex( c => c.Item1.InternalId == data.InternalId );
            var dataFromList = m_downloadingList[ index ];
            var isCached     = dataFromList.Item2;

            m_downloadingList.RemoveAt( index );

            var sb = new StringBuilder();

            sb.Append( isCached ? "キャッシュからロード成功" : "サーバからダウンロード成功" );
            sb.Append( "：" );
            sb.AppendLine( Path.GetFileName( data.Url ) );
            sb.AppendLine( data.ToPrettyJson() );

            Debug.Log( sb.ToString() );
        };

        // 読み込みに失敗してリトライする時に呼び出されます
        // 現在のリトライ回数とリトライの最大回数を取得できます
        AssetBundleResourceWithCallback.OnRetry +=
            (
                data,
                retryCount,
                maxRetryCount
            ) =>
            {
                var sb = new StringBuilder();

                sb.Append( "リトライ " );
                sb.Append( retryCount.ToString() );
                sb.Append( " / " );
                sb.Append( maxRetryCount.ToString() );
                sb.Append( " 回目：" );
                sb.AppendLine( Path.GetFileName( data.Url ) );
                sb.AppendLine( data.ToPrettyJson() );

                Debug.LogWarning( sb.ToString() );

                if ( data.Url.Contains( "localhost" ) )
                {
                    Debug.LogWarning( "ローカルサーバが起動しているか確認してください" );
                }
            };

        // 読み込んだアセットバンドルがアンロードされた時に呼び出されます
        AssetBundleResourceWithCallback.OnUnload += data =>
        {
            var sb = new StringBuilder();

            sb.Append( "アンロード：" );
            sb.AppendLine( Path.GetFileName( data.Url ) );
            sb.AppendLine( data.ToPrettyJson() );

            Debug.Log( sb.ToString() );
        };

        // 読み込みに失敗した場合に呼び出されます
        // リトライする場合はすべてのリトライに失敗した時に呼び出されます
        AssetBundleResourceWithCallback.OnFailure +=
            (
                data,
                onRetry,
                onComplete
            ) =>
            {
                var index = m_downloadingList.FindIndex( c => c.Item1.InternalId == data.InternalId );
                m_downloadingList.RemoveAt( index );

                var sb = new StringBuilder();

                sb.Append( "リトライ失敗：" );
                sb.AppendLine( Path.GetFileName( data.Url ) );
                sb.AppendLine( data.ToPrettyJson() );

                Debug.LogWarning( sb.ToString() );

                onComplete();
            };

        // 最大同時通信数を設定します
        AssetBundleResourceWithCallback.SetMaxConcurrentRequests( 10 );
    }
}
```

## 補足

* Addressable 1.16.1 をベースにしています  
