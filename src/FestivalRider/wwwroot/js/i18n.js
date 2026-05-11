window.festivalRiderI18n = {
    getNavigatorLanguage: function () {
        return navigator.language || "";
    },
    setHtmlLang: function (tag) {
        document.documentElement.lang = tag;
    }
};
