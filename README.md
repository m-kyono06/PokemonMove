# PokemonMove

Android 向けの、DS・3DS作品のポケモンをまとめて管理するアプリ。
C# / .NET MAUI と PKHeX.Core を使用します。

## 現在の機能（0.2.0）

- HGSS の通常セーブを Android のファイル選択から読み込む
- 主人公名・作品名・手持ち・18ボックス（各30枠）を表示
- ポケモンの日本語名・ニックネーム・レベル・タイプ・技・色違いを表示
- 非対応形式、主人公・手持ち・ボックス領域のチェックサム不一致を拒否
- 表示しない追加領域だけの不一致は、警告を表示して読み取りを継続

- ポケモンをローカルバンクへコピーして保管（元のセーブには残ります）
- 再起動後もバンク一覧・詳細・コピー元を表示、名前で検索
- 同じポケモンデータの重複保管を防止
- バンクのバックアップ書き出し・取り込み（重複を除いて追加）と直前の状態への復元

セーブは読み取り専用です。元ファイルの書き込み、自動的なクラウド送信は行いません。
ゲームへの引き出し・移動、収集図鑑、3DS作品は未実装です。

## 保管の使い方

1. セーブを開き、手持ちまたはボックス内のポケモンを選びます。
2. 詳細の「バンクにコピーして保管」を押します。
3. 「ローカルバンクを開く」で保管したポケモンを確認します。

バンクにはHGSSのポケモン本体（PK4、136バイト）とコピー元・保管日時を保存します。
表示用の名前だけではなく、個体値・努力値・技・親情報なども保持します。
データ検証に失敗した個体は保管できません。同一性は個体データ全体で判断します。
コピー元でレベル等が変わったデータは別の記録になります。

バンクはアプリ専用領域の `bank/bank.json` に保存し、更新時に前回分を `.bak` に残します。
「直前の状態へ戻す」はバンク全体をそのバックアップに置き換えます。

**アプリのアンインストールや端末変更の前に「バックアップを書き出す」で外部へ保存してください。**
共有先のアプリで保存を完了する必要があります。バックアップにはポケモン・トレーナー情報が含まれます。
開発版はビルドごとにデバッグ署名が変わる場合があります。更新時に旧版の削除が必要なら、必ず先にバックアップしてください。

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
 dotnet test tests/PokemonMove.Infrastructure.Tests/PokemonMove.Infrastructure.Tests.csproj

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
- `PokemonMove.Infrastructure`：ローカルバンク・原子的な保存・バックアップ
- `PokemonMove.App`：Android 用 MAUI 画面とファイル選択
- `PokemonMove.Engine.Tests`：生成した HG/SS データ、DSV、破損・非対応データのテスト
- `PokemonMove.Infrastructure.Tests`：再読込・重複防止・バックアップ復元・保存失敗時の保護

実際のユーザーのセーブや ROM はリポジトリに含めません。
テストは PKHeX で生成するデータを使用し、入力が変化しないことも検証します。

## 今後の順序

1. Android実機でバンクの保管・再起動・バックアップ取り込みを確認
2. セーブのバックアップ・復元を伴う引き出し／移動
3. 所有ポケモンから集計する収集図鑑
4. 対応する DS・3DS 作品と世代間移動を拡充

依存ライブラリと配布時の注意は [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) を参照。

## ライセンス

GPL-3.0-or-later。全文は [LICENSE](LICENSE) を参照してください。
