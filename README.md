# PokemonMove

Android 向けの、DS・3DS作品のポケモンをまとめて管理するアプリ。
C# / .NET MAUI と PKHeX.Core を使用します。

## 現在の機能（0.1.1）

- HGSS の通常セーブを Android のファイル選択から読み込む
- 主人公名・作品名・手持ち・18ボックス（各30枠）を表示
- ポケモンの日本語名・ニックネーム・レベル・タイプ・技・色違いを表示
- 非対応形式、主人公・手持ち・ボックス領域のチェックサム不一致を拒否
- 表示しない追加領域だけの不一致は、警告を表示して読み取りを継続

読み取り専用です。元ファイルの書き込み、クラウドへの送信は行いません。
バンクへの保存、ポケモンの移動、収集図鑑、3DS作品は未実装です。

## セーブの選択

ゲーム内でレポートを書いた後、エミュレータを終了して通常セーブを選択します。

- melonDS：512 KiB の `.sav`
- DraStic：バッテリーセーブの `.dsv`（通常セーブまたは DeSmuME フッター形式）
- ステートセーブ、ROM、圧縮ファイルには対応していません。

Android のファイル選択を利用するため、広範なストレージ権限は要求しません。
エミュレータの保存場所が選択できない場合は、そのエミュレータから通常セーブを
共有ストレージへエクスポートするか、コピーして選択してください。
実際の melonDS / DraStic 出力と実機での操作確認は今後必要です。

## 開発環境

- .NET SDK 10.0.403（`global.json`）
- `dotnet workload install maui-android --skip-manifest-update`
- JDK 21
- Android SDK：platforms;android-36、build-tools;36.0.0、platform-tools
- Android 8.0（API 26）以降を対象

```sh
# Android SDK がなくても解析処理のテストは実行可能
 dotnet test tests/PokemonMove.Engine.Tests/PokemonMove.Engine.Tests.csproj

# arm64 Android のデバッグAPK
 dotnet build src/PokemonMove.App/PokemonMove.App.csproj \
   -p:RuntimeIdentifier=android-arm64 \
   -p:AndroidSdkDirectory="$ANDROID_HOME" \
   -p:JavaSdkDirectory="$JAVA_HOME"
```

APK は `src/PokemonMove.App/bin/Debug/net10.0-android/android-arm64/` に生成されます。
デバッグ署名の APK です。公開用の署名・配布は別途準備します。

このクラウド環境の配置：

```sh
export PATH="/workspace/.dotnet:$PATH"
export ANDROID_HOME=/workspace/android-sdk
export JAVA_HOME=/workspace/java/jdk-21.0.9+10
```

## 構成

- `PokemonMove.Domain`：画面で扱う読み取り結果と解析インターフェース
- `PokemonMove.Engine`：PKHeX.Core を使った HGSS 解析
- `PokemonMove.App`：Android 用 MAUI 画面とファイル選択
- `PokemonMove.Engine.Tests`：生成した HG/SS データ、DSV、破損・非対応データのテスト

実際のユーザーのセーブや ROM はリポジトリに含めません。
テストは PKHeX で生成するデータを使用し、入力が変化しないことも検証します。

## 今後の順序

1. HGSS の実機・エミュレータ出力で読み取り確認
2. ローカルバンクと、バックアップ・復元を伴う預け入れ／引き出し
3. HGSS 間の移動と、所有ポケモンから集計する収集図鑑
4. 対応する DS・3DS 作品と世代間移動を拡充

依存ライブラリと配布時の注意は [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) を参照。

## ライセンス

GPL-3.0-or-later。全文は [LICENSE](LICENSE) を参照してください。
