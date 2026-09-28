function videoManager() {
    return {
        videos: [],
        tags: [],
        rules: [],
        searchQuery: '',
        favoriteOnly: false,
        isScanning: false,
        selectedVideo: null,
        viewMode: 'grid', // 'grid' または 'list'
        showThumbnails: true, // サムネイル表示・非表示
        gridSize: 'md', // 'sm' | 'md' | 'lg'
        sortBy: 'createdAt', // 'createdAt' | 'fileName' | 'duration' | 'fileSize' | 'accessCount'
        sortOrder: 'desc', // 'desc' | 'asc'
        showToolbar: true, // 表示設定ツールバーの開閉状態
        sidebarOpen: true, // サイドバーの開閉状態
        selectedVideoIds: [], // 複数選択中の動画IDリスト
        collapsedTags: [], // 折りたたまれたタグIDリスト

        batchTagModal: { show: false, mode: '' }, // 一括タグ追加/削除モード
        hasAutoScanned: false, // 初回自動スキャン判定用

        // 表示モード (ページング / インクリメンタル)
        displayMode: 'paging',       // 'paging' | 'incremental'
        pageSize: 50,                // 1ページあたりの表示件数 (20 / 50 / 100)
        currentPage: 1,              // 現在ページ (1-indexed)
        incrementalCount: 20,        // インクリメンタルモードでの現在の表示件数
        incrementalStep: 20,         // 「もっと表示」1回あたりの追加件数
        
        // トースト通知
        toast: {
            show: false,
            message: '',
            type: 'success'
        },
        showToast(message, type = 'success') {
            this.toast.message = message;
            this.toast.type = type;
            this.toast.show = true;
            setTimeout(() => {
                this.toast.show = false;
            }, 4000);
        },
        
        // モーダル用バインドデータ
        tagModal: {
            show: false,
            isEdit: false,
            id: '',
            name: '',
            colorCode: '#6366f1',
            parentId: '',
            isFromRule: false
        },

        ruleModal: {
            show: false,
            isEdit: false,
            id: '',
            pattern: '',
            tagId: '',
            matchType: 'partial',
            targetField: 'fileName',
            minSize: null,
            maxSize: null,
            showAdvanced: false
        },
        videoTagModal: {
            show: false,
            video: null
        },
        smartFolders: [],
        smartFolderModal: {
            show: false,
            name: '',
            query: ''
        },
        bulkModal: {
            show: false,
            activeTab: 'tags', // 'tags' | 'rules' | 'smartFolders'
            tsvInput: ''
        },
        analyticsModal: {
            show: false,
            activeTab: 'ranking', // 'ranking' | 'recommendations' | 'logs'
            ranking: [],
            keywords: [],
            recommendations: [],
            logs: [],
            activeLogId: null
        },


        init() {
            // Lucideアイコンの初期描画
            setTimeout(() => { lucide.createIcons(); }, 100);
            
            // 初期データロード
            this.fetchTags();
            this.fetchRules();
            this.fetchSmartFolders();
            this.fetchVideos();

            // クエリ変更の監視 (インクリメンタルサーチ)
            this.$watch('searchQuery', () => {
                this.fetchVideos();
            });

            // 描画される動画が更新されたら自動的にLucideアイコンを生成する
            this.$watch('pagedVideos', () => {
                this.$nextTick(() => {
                    lucide.createIcons();
                });
            });

            // タグ、ルール、スマートフォルダ、ダッシュボード分析が更新された際も自動生成
            this.$watch('sortedTagTree', () => {
                this.$nextTick(() => {
                    lucide.createIcons();
                });
            });
            this.$watch('rules', () => {
                this.$nextTick(() => {
                    lucide.createIcons();
                });
            });
            this.$watch('smartFolders', () => {
                this.$nextTick(() => {
                    lucide.createIcons();
                });
            });
            this.$watch('analysis', () => {
                this.$nextTick(() => {
                    lucide.createIcons();
                });
            });
        },

        // クライアントサイドでのソート適用済み動画リスト
        get sortedVideos() {
            return [...this.videos].sort((a, b) => {
                let valA = a[this.sortBy];
                let valB = b[this.sortBy];
                
                if (this.sortBy === 'createdAt') {
                    valA = new Date(valA).getTime();
                    valB = new Date(valB).getTime();
                }
                
                if (typeof valA === 'string') {
                    return this.sortOrder === 'asc' 
                        ? valA.localeCompare(valB) 
                        : valB.localeCompare(valA);
                }
                
                return this.sortOrder === 'asc' ? valA - valB : valB - valA;
            });
        },

        // ページング・インクリメンタル適用後の表示動画リスト
        get pagedVideos() {
            const sorted = this.sortedVideos;
            if (this.displayMode === 'paging') {
                const start = (this.currentPage - 1) * this.pageSize;
                return sorted.slice(start, start + this.pageSize);
            } else {
                // インクリメンタル
                return sorted.slice(0, this.incrementalCount);
            }
        },

        // ページング: 総ページ数
        get totalPages() {
            return Math.max(1, Math.ceil(this.sortedVideos.length / this.pageSize));
        },

        // インクリメンタル: さらに表示できる動画があるか
        get hasMore() {
            return this.incrementalCount < this.sortedVideos.length;
        },

        // インクリメンタルモード: さらに表示
        loadMore() {
            this.incrementalCount = Math.min(
                this.incrementalCount + this.incrementalStep,
                this.sortedVideos.length
            );
        },

        // ページング: ページ移動
        goToPage(page) {
            if (page < 1 || page > this.totalPages) return;
            this.currentPage = page;
            // ページ移動後にトップへスクロール
            this.$nextTick(() => {
                document.querySelector('main')?.scrollTo({ top: 0, behavior: 'smooth' });
            });
        },

        // ページサイズ変更時はページ1に戻す
        changePageSize(size) {
            this.pageSize = size;
            this.currentPage = 1;
        },

        // API連携: 動画一覧取得
        async fetchVideos() {
            let url = '/api/videos';
            let queryParts = [];

            // 検索クエリがある場合
            if (this.searchQuery) {
                queryParts.push(this.searchQuery);
            }
            // お気に入りフィルターがある場合
            if (this.favoriteOnly) {
                queryParts.push('is:favorite');
            }

            if (queryParts.length > 0) {
                url += `?q=${encodeURIComponent(queryParts.join(' '))}`;
            }

            try {
                let res = await fetch(url);
                if (res.ok) {
                    this.videos = await res.json();
                    // 検索・フィルタ変更時はページと表示件数をリセット
                    this.currentPage = 1;
                    this.incrementalCount = this.incrementalStep;
                    console.log("fetchVideos success. Videos count:", this.videos.length, this.videos);
                    setTimeout(() => { lucide.createIcons(); }, 50);

                    // 初回アクセス時に登録動画が0件で、まだ自動スキャンを行っていない場合
                    if (this.videos.length === 0 && !this.hasAutoScanned && !this.searchQuery && !this.favoriteOnly) {
                        this.hasAutoScanned = true;
                        this.triggerScan();
                    }
                }
            } catch (e) {
                console.error("Failed to fetch videos", e);
            }

        },

        // API連携: タグ一覧取得
        async fetchTags() {
            try {
                let res = await fetch('/api/tags');
                if (res.ok) {
                    this.tags = await res.json();
                    console.log("fetchTags success. Tags count:", this.tags.length, this.tags);
                }
            } catch (e) {
                console.error("Failed to fetch tags", e);
            }
        },

        // API連携: ルール一覧取得
        async fetchRules() {
            try {
                let res = await fetch('/api/rules');
                if (res.ok) {
                    this.rules = await res.json();
                    setTimeout(() => { lucide.createIcons(); }, 50);
                }
            } catch (e) {
                console.error("Failed to fetch rules", e);
            }
        },

        // API連携: スマートフォルダ一覧取得
        async fetchSmartFolders() {
            try {
                let res = await fetch('/api/smart-folders');
                if (res.ok) {
                    this.smartFolders = await res.json();
                    setTimeout(() => { lucide.createIcons(); }, 50);
                }
            } catch (e) {
                console.error("Failed to fetch smart folders", e);
            }
        },

        openSmartFolderModal() {
            this.smartFolderModal.query = this.searchQuery;
            this.smartFolderModal.name = '';
            this.smartFolderModal.show = true;
            setTimeout(() => {
                let input = document.getElementById('smart-folder-name-input');
                if (input) input.focus();
            }, 100);
        },

        async saveSmartFolder() {
            if (!this.smartFolderModal.name.trim()) {
                this.showToast("名前を入力してください。", 'error');
                return;
            }
            try {
                let res = await fetch('/api/smart-folders', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({
                        name: this.smartFolderModal.name.trim(),
                        query: this.smartFolderModal.query
                    })
                });
                if (res.ok) {
                    this.showToast("スマートフォルダを保存しました。", 'success');
                    this.smartFolderModal.show = false;
                    await this.fetchSmartFolders();
                } else {
                    this.showToast("保存に失敗しました。", 'error');
                }
            } catch (e) {
                this.showToast("通信エラーが発生しました。", 'error');
            }
        },

        async deleteSmartFolder(id) {
            if (!confirm("このスマートフォルダを削除しますか？")) return;
            try {
                let res = await fetch(`/api/smart-folders/${id}`, {
                    method: 'DELETE'
                });
                if (res.ok) {
                    this.showToast("スマートフォルダを削除しました。", 'success');
                    await this.fetchSmartFolders();
                } else {
                    this.showToast("削除に失敗しました。", 'error');
                }
            } catch (e) {
                this.showToast("通信エラーが発生しました。", 'error');
            }
        },

        applySmartFolder(query) {
            this.searchQuery = query;
            this.showToast(`スマートフォルダを適用しました: "${query}"`, 'success');
        },

        openAnalyticsModal() {
            this.analyticsModal.show = true;
            this.analyticsModal.activeTab = 'ranking';
            this.fetchVideoRanking();
        },

        async fetchVideoRanking() {
            try {
                let res = await fetch('/api/analysis/ranking');
                if (res.ok) {
                    this.analyticsModal.ranking = await res.json();
                    setTimeout(() => { lucide.createIcons(); }, 50);
                } else {
                    this.showToast("ランキングの取得に失敗しました。", 'error');
                }
            } catch (e) {
                this.showToast("通信エラーが発生しました。", 'error');
            }
        },

        async fetchVideoRecommendations() {
            try {
                let res = await fetch('/api/analysis/recommendations');
                if (res.ok) {
                    let data = await res.json();
                    this.analyticsModal.keywords = data.keywords;
                    this.analyticsModal.recommendations = data.videos;
                    setTimeout(() => { lucide.createIcons(); }, 50);
                } else {
                    this.showToast("おすすめ動画の取得に失敗しました。", 'error');
                }
            } catch (e) {
                this.showToast("通信エラーが発生しました。", 'error');
            }
        },

        async fetchVideoLogs() {
            try {
                let res = await fetch('/api/analysis/logs');
                if (res.ok) {
                    this.analyticsModal.logs = await res.json();
                    this.analyticsModal.activeLogId = null;
                    setTimeout(() => { lucide.createIcons(); }, 50);
                } else {
                    this.showToast("例外エラーログの取得に失敗しました。", 'error');
                }
            } catch (e) {
                this.showToast("通信エラーが発生しました。", 'error');
            }
        },


        openBulkModal() {
            this.bulkModal.tsvInput = '';
            this.bulkModal.show = true;
            this.exportBulkData(); // 初期表示でエクスポートデータを表示しておく
        },

        async importBulkData() {
            if (!this.bulkModal.tsvInput.trim()) {
                this.showToast("インポートするデータを入力してください。", 'error');
                return;
            }
            let url = '';
            switch (this.bulkModal.activeTab) {
                case 'tags': url = '/api/tags/import'; break;
                case 'rules': url = '/api/rules/import'; break;
                case 'smartFolders': url = '/api/smart-folders/import'; break;
            }
            try {
                let res = await fetch(url, {
                    method: 'POST',
                    headers: { 'Content-Type': 'text/plain; charset=utf-8' },
                    body: this.bulkModal.tsvInput.trim()
                });
                if (res.ok) {
                    let resultList = await res.json();
                    this.showToast(`${resultList.length} 件の設定をインポートしました。`, 'success');
                    this.bulkModal.show = false;
                    // 再ロード
                    await this.fetchTags();
                    await this.fetchRules();
                    await this.fetchSmartFolders();
                    await this.fetchVideos();
                } else {
                    let errMsg = await res.text();
                    this.showToast(`インポートに失敗しました: ${errMsg}`, 'error');
                }
            } catch (e) {
                this.showToast("通信エラーが発生しました。", 'error');
            }
        },

        exportBulkData() {
            let tsv = '';
            if (this.bulkModal.activeTab === 'tags') {
                tsv = "id\tname\tcolorCode\tparentId\n";
                this.tags.forEach(t => {
                    tsv += `${t.id}\t${t.name}\t${t.colorCode || ''}\t${t.parentId || ''}\n`;
                });
            } else if (this.bulkModal.activeTab === 'rules') {
                tsv = "id\tpattern\ttagId\tmatchType\ttargetField\tminSize\tmaxSize\n";
                this.rules.forEach(r => {
                    tsv += `${r.id}\t${r.pattern}\t${r.tagId}\t${r.matchType || 'partial'}\t${r.targetField || 'fileName'}\t${r.minSize || ''}\t${r.maxSize || ''}\n`;
                });
            } else if (this.bulkModal.activeTab === 'smartFolders') {
                tsv = "id\tname\tquery\n";
                this.smartFolders.forEach(sf => {
                    tsv += `${sf.id}\t${sf.name}\t${sf.query}\n`;
                });
            }
            this.bulkModal.tsvInput = tsv;
        },

        copyToClipboard(text) {
            navigator.clipboard.writeText(text).then(() => {
                this.showToast(`IDをコピーしました: ${text}`, 'success');
            }).catch(err => {
                this.showToast("コピーに失敗しました。", 'error');
            });
        },

        toggleTagCollapse(tagId) {
            if (this.collapsedTags.includes(tagId)) {
                this.collapsedTags = this.collapsedTags.filter(id => id !== tagId);
            } else {
                this.collapsedTags.push(tagId);
            }
        },

        isTagCollapsed(tagId) {
            return this.collapsedTags.includes(tagId);
        },

        // タグの階層分類ヘルパー (深さ優先探索によるソートツリーの生成)
        get sortedTagTree() {
            const result = [];
            const traverse = (parentId, level) => {
                if (this.collapsedTags.includes(parentId)) return; // 閉じられている場合は子孫を走査しない
                const children = this.tags.filter(t => t.parentId === parentId);
                children.sort((a, b) => a.name.localeCompare(b.name));
                children.forEach(tag => {
                    const hasChildren = this.tags.some(t => t.parentId === tag.id);
                    result.push({ ...tag, level: level, hasChildren: hasChildren });
                    traverse(tag.id, level + 1);
                });
            };
            const roots = this.tags.filter(t => !t.parentId);
            roots.sort((a, b) => a.name.localeCompare(b.name));
            roots.forEach(tag => {
                const hasChildren = this.tags.some(t => t.parentId === tag.id);
                result.push({ ...tag, level: 0, hasChildren: hasChildren });
                traverse(tag.id, 1);
            });
            return result;
        },


        get eligibleParentTags() {
            // 自分自身、および自分のすべての下位子孫タグを除外して循環参照を防止
            if (!this.tagModal.isEdit) return this.sortedTagTree;
            
            const getDescendantIds = (tagId) => {
                let ids = [];
                const children = this.tags.filter(t => t.parentId === tagId);
                children.forEach(c => {
                    ids.push(c.id);
                    ids = ids.concat(getDescendantIds(c.id));
                });
                return ids;
            };
            
            const descendants = getDescendantIds(this.tagModal.id);
            return this.sortedTagTree.filter(t => t.id !== this.tagModal.id && !descendants.includes(t.id));
        },

        // 検索のユーティリティ
        filterByTag(tagName) {
            this.searchQuery = `tag:${tagName}`;
        },

        toggleFavoriteFilter() {
            this.favoriteOnly = !this.favoriteOnly;
            this.fetchVideos();
        },

        clearFilters() {
            console.log("clearFilters called. Current searchQuery:", this.searchQuery);
            const queryChanged = this.searchQuery !== '';
            const favChanged = this.favoriteOnly !== false;
            
            this.searchQuery = '';
            this.favoriteOnly = false;
            console.log("clearFilters finished. New searchQuery:", this.searchQuery);
            
            if (!queryChanged && favChanged) {
                this.fetchVideos();
            }
        },

        // API連携: スキャン同期
        async triggerScan() {
            this.isScanning = true;
            try {
                let res = await fetch('/api/scan', { method: 'POST' });
                if (res.ok) {
                    let data = await res.json();
                    this.showToast(`同期完了: ${data.addedCount} 件の新規動画が追加されました。`, 'success');
                    this.fetchVideos();
                } else {
                    this.showToast("スキャン同期に失敗しました。", 'error');
                }
            } catch (e) {
                this.showToast("通信エラーが発生しました。", 'error');
            } finally {
                this.isScanning = false;
                setTimeout(() => { lucide.createIcons(); }, 50);
            }
        },

        async stopMediaProcessing() {
            try {
                let res = await fetch('/api/media/stop', { method: 'POST' });
                if (res.ok) {
                    this.showToast("生成処理を停止しました。", 'success');
                } else {
                    this.showToast("停止処理に失敗しました。", 'error');
                }
            } catch (e) {
                this.showToast("通信エラーが発生しました。", 'error');
            }
        },

        // 動画プレイヤー操作
        playVideo(video) {
            if (video) {
                video.accessCount = (video.accessCount || 0) + 1;
                video.lastAccessedAt = new Date().toISOString();
                
                // バックエンドの再生履歴記録APIをキック (シーク時の多重カウント防止)
                fetch(`/api/videos/${video.id}/access`, { method: 'POST' })
                    .catch(err => console.error("Failed to record access:", err));

                // スマホ（モバイル）判定 (UserAgent or 画面幅が md 768px 未満)
                const isMobile = /Android|webOS|iPhone|iPad|iPod|BlackBerry|IEMobile|Opera Mini/i.test(navigator.userAgent) || window.innerWidth < 768;
                if (isMobile) {
                    // ストリームURLに直接遷移して、OSプレイヤーや外部アプリへ再生委譲
                    window.location.href = `/api/videos/${video.id}/stream`;
                } else {
                    this.selectedVideo = video;
                }
            }
        },


        closeVideo() {
            let player = document.getElementById('mainPlayer');
            if (player) {
                player.pause();
                player.src = "";
            }
            this.selectedVideo = null;
        },

        clearVideoAccess(videoId) {
            if (!confirm("この動画の再生回数をクリアしますか？")) return;
            fetch(`/api/videos/${videoId}/access/clear`, { method: 'POST' })
                .then(res => {
                    if (res.ok) {
                        this.showToast("再生回数をクリアしました。", 'success');
                        
                        // ローカル状態の更新
                        const video = this.videos.find(v => v.id === videoId);
                        if (video) {
                            video.accessCount = 0;
                            video.lastAccessedAt = null;
                        }
                        if (this.selectedVideo && this.selectedVideo.id === videoId) {
                            this.selectedVideo.accessCount = 0;
                            this.selectedVideo.lastAccessedAt = null;
                        }
                        // 統計モーダルの情報も再ロード
                        if (this.analyticsModal.show) {
                            this.fetchVideoRanking();
                            this.fetchVideoRecommendations();
                        }
                    } else {
                        this.showToast("再生回数のクリアに失敗しました。", 'error');
                    }
                })
                .catch(err => {
                    console.error(err);
                    this.showToast("通信エラーが発生しました。", 'error');
                });
        },


        // タグの作成・編集ダイアログ
        openCreateTagModal(isFromRule = false) {
            const colors = ['#6366f1', '#ec4899', '#f43f5e', '#10b981', '#f59e0b', '#3b82f6', '#8b5cf6', '#06b6d4', '#14b8a6', '#a855f7', '#f97316'];
            const randomColor = colors[Math.floor(Math.random() * colors.length)];
            this.tagModal = {
                show: true,
                isEdit: false,
                id: '',
                name: '',
                colorCode: randomColor,
                parentId: '',
                isFromRule: isFromRule
            };
        },

        openEditTagModal(tag) {
            this.tagModal = {
                show: true,
                isEdit: true,
                id: tag.id,
                name: tag.name,
                colorCode: tag.colorCode,
                parentId: tag.parentId || '',
                isFromRule: false
            };
        },

        async saveTag() {
            if (!this.tagModal.name.trim()) {
                this.showToast("タグ名を入力してください。", "error");
                return;
            }
            try {
                if (this.tagModal.isEdit) {
                    // 編集（階層・親の更新）
                    let res = await fetch(`/api/tags/${this.tagModal.id}`, {
                        method: 'PATCH',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ parentId: this.tagModal.parentId || null })
                    });
                    if (res.ok) {
                        this.showToast("タグを更新しました。", "success");
                        this.tagModal.show = false;
                        await this.fetchTags();
                        this.fetchVideos();
                    } else {
                        this.showToast("タグの更新に失敗しました。", "error");
                    }
                } else {
                    // 新規作成
                    let res = await fetch('/api/tags', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({
                            name: this.tagModal.name.trim(),
                            colorCode: this.tagModal.colorCode,
                            parentId: this.tagModal.parentId || null
                        })
                    });
                    if (res.ok) {
                        let newTag = await res.json();
                        this.showToast("タグを作成しました。", "success");
                        this.tagModal.show = false;
                        await this.fetchTags();
                        
                        // ルール作成ダイアログから呼ばれた場合はそのタグをセット
                        if (this.tagModal.isFromRule) {
                            this.ruleModal.tagId = newTag.id;
                        }
                    } else {
                        this.showToast("タグの作成に失敗しました。", "error");
                    }
                }
            } catch (e) {
                this.showToast("通信エラーが発生しました。", "error");
            }
        },

        async deleteTag(tagId) {
            if (!confirm("本当にこのタグを削除しますか？子タグの親指定は解除されます。")) return;
            try {
                let res = await fetch(`/api/tags/${tagId}`, { method: 'DELETE' });
                if (res.ok) {
                    this.fetchTags();
                    this.fetchVideos();
                }
            } catch (e) {
                alert("通信エラーが発生しました。");
            }
        },

        // ルールダイアログ
        openCreateRuleModal() {
            this.ruleModal = {
                show: true,
                isEdit: false,
                id: '',
                pattern: '',
                tagId: '',
                matchType: 'partial',
                targetField: 'fileName',
                minSize: null,
                maxSize: null,
                showAdvanced: false
            };
            setTimeout(() => { lucide.createIcons(); }, 50);
        },

        openEditRuleModal(rule) {
            this.ruleModal = {
                show: true,
                isEdit: true,
                id: rule.id,
                pattern: rule.pattern,
                tagId: rule.tagId,
                matchType: rule.matchType || 'partial',
                targetField: rule.targetField || 'fileName',
                minSize: rule.minSize || null,
                maxSize: rule.maxSize || null,
                showAdvanced: !!(rule.matchType && rule.matchType !== 'partial' || rule.targetField && rule.targetField !== 'fileName' || rule.minSize || rule.maxSize)
            };
            setTimeout(() => { lucide.createIcons(); }, 50);
        },

        async saveRule() {
            if (!this.ruleModal.pattern.trim() && !this.ruleModal.minSize && !this.ruleModal.maxSize) {
                this.showToast("パターンまたはサイズ条件を入力してください。", "error");
                return;
            }
            if (!this.ruleModal.tagId) {
                this.showToast("タグを選択してください。", "error");
                return;
            }
            try {
                const body = {
                    pattern: this.ruleModal.pattern,
                    tagId: this.ruleModal.tagId,
                    matchType: this.ruleModal.matchType || 'partial',
                    targetField: this.ruleModal.targetField || 'fileName',
                    minSize: this.ruleModal.minSize || null,
                    maxSize: this.ruleModal.maxSize || null
                };
                if (this.ruleModal.isEdit) {
                    let res = await fetch(`/api/rules/${this.ruleModal.id}`, {
                        method: 'PUT',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify(body)
                    });
                    if (res.ok) {
                        this.ruleModal.show = false;
                        this.fetchRules();
                        this.showToast("ルールを更新しました。", "success");
                        setTimeout(() => { this.fetchVideos(); }, 1000);
                    } else {
                        this.showToast("ルールの更新に失敗しました。", "error");
                    }
                } else {
                    let res = await fetch('/api/rules', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify(body)
                    });
                    if (res.ok) {
                        this.ruleModal.show = false;
                        this.fetchRules();
                        this.showToast("ルールを作成しました。", "success");
                        setTimeout(() => { this.fetchVideos(); }, 1000);
                    } else {
                        this.showToast("ルールの作成に失敗しました。", "error");
                    }
                }
            } catch (e) {
                this.showToast("通信エラーが発生しました。", "error");
            }
        },

        async deleteRule(ruleId) {
            try {
                let res = await fetch(`/api/rules/${ruleId}`, { method: 'DELETE' });
                if (res.ok) {
                    this.fetchRules();
                }
            } catch (e) {
                alert("通信エラーが発生しました。");
            }
        },

        // お気に入りトグル
        async toggleFavorite(video) {
            const newState = !video.isFavorite;
            try {
                let res = await fetch(`/api/videos/${video.id}/favorite`, {
                    method: 'PUT',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ isFavorite: newState })
                });
                if (res.ok) {
                    // ローカルの状態を即時更新（再フェッチなし）
                    const idx = this.videos.findIndex(v => v.id === video.id);
                    if (idx >= 0) {
                        this.videos[idx] = { ...this.videos[idx], isFavorite: newState };
                        this.videos = [...this.videos]; // reactivity trigger
                    }
                    // 再生モーダル内の表示も連動更新する
                    if (this.selectedVideo && this.selectedVideo.id === video.id) {
                        this.selectedVideo = { ...this.selectedVideo, isFavorite: newState };
                    }
                }
            } catch (e) {
                this.showToast("通信エラーが発生しました。", "error");
            }
        },

        // 複数選択バッチ操作
        clearSelection() {
            this.selectedVideoIds = [];
        },

        async batchSetFavorite(isFavorite) {
            if (this.selectedVideoIds.length === 0) return;
            try {
                let res = await fetch('/api/videos/batch/favorite', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ videoIds: this.selectedVideoIds, isFavorite: isFavorite })
                });
                if (res.ok) {
                    this.showToast(`${this.selectedVideoIds.length} 件のお気に入り状態を更新しました。`, 'success');
                    this.selectedVideoIds = [];
                    this.fetchVideos();
                }
            } catch (e) {
                this.showToast("通信エラーが発生しました。", "error");
            }
        },

        batchTagModalData: { show: false, mode: 'add', tagId: '' },

        openBatchTagModal(mode) {
            this.batchTagModalData = { show: true, mode: mode, tagId: '' };
            setTimeout(() => { lucide.createIcons(); }, 50);
        },

        async executeBatchTag() {
            if (!this.batchTagModalData.tagId || this.selectedVideoIds.length === 0) return;
            const url = this.batchTagModalData.mode === 'add'
                ? '/api/videos/batch/tags/add'
                : '/api/videos/batch/tags/remove';
            try {
                let res = await fetch(url, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ videoIds: this.selectedVideoIds, tagId: this.batchTagModalData.tagId })
                });
                if (res.ok) {
                    const action = this.batchTagModalData.mode === 'add' ? '追加' : '削除';
                    this.showToast(`${this.selectedVideoIds.length} 件の動画のタグを${action}しました。`, 'success');
                    this.batchTagModalData.show = false;
                    this.selectedVideoIds = [];
                    this.fetchVideos();
                    this.fetchTags();
                }
            } catch (e) {
                this.showToast("通信エラーが発生しました。", "error");
            }
        },


        // 動画タグ手動紐付けダイアログ
        openAddTagToVideoModal(video) {
            this.videoTagModal = {
                show: true,
                video: video
            };
        },

        isVideoTagged(tagId) {
            if (!this.videoTagModal.video) return false;
            return this.videoTagModal.video.tags.some(t => t.id === tagId);
        },

        async toggleVideoTag(tagId, isChecked) {
            if (!this.videoTagModal.video) return;
            let videoId = this.videoTagModal.video.id;
            
            let url = `/api/videos/${videoId}/tags`;
            let method = isChecked ? 'POST' : 'DELETE';
            
            try {
                let res = await fetch(url, {
                    method: method,
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ tagId: tagId })
                });
                if (res.ok) {
                    this.fetchVideos();
                    this.fetchTags();
                    // モーダルの表示対象の動画オブジェクトを再取得して更新
                    setTimeout(() => {
                        let updatedVideo = this.videos.find(v => v.id === videoId);
                        if (updatedVideo) {
                            this.videoTagModal.video = updatedVideo;
                        }
                    }, 100);
                } else {
                    console.error("toggleVideoTag API failed. Status:", res.status);
                }
            } catch (e) {
                console.error("toggleVideoTag error", e);
            }
        },

        // 表示用ヘルパー
        getTagName(tagId) {
            let tag = this.tags.find(t => t.id === tagId);
            return tag ? tag.name : '不明なタグ';
        },

        getTagColor(tagId) {
            let tag = this.tags.find(t => t.id === tagId);
            return tag ? tag.colorCode : '#71717a';
        },

        formatDuration(seconds) {
            let h = Math.floor(seconds / 3600);
            let m = Math.floor((seconds % 3600) / 60);
            let s = seconds % 60;
            if (h > 0) {
                return `${h}:${m.toString().padStart(2, '0')}:${s.toString().padStart(2, '0')}`;
            }
            return `${m}:${s.toString().padStart(2, '0')}`;
        },

        formatSize(bytes) {
            if (bytes === 0) return '0 B';
            let k = 1024;
            let sizes = ['B', 'KB', 'MB', 'GB', 'TB'];
            let i = Math.floor(Math.log(bytes) / Math.log(k));
            return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
        },

        formatDate(isoString) {
            try {
                let d = new Date(isoString);
                return `${d.getFullYear()}/${(d.getMonth() + 1).toString().padStart(2, '0')}/${d.getDate().toString().padStart(2, '0')}`;
            } catch (e) {
                return isoString;
            }
        }
    };
}
