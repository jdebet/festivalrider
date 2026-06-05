window.bandGrid = {
    scrollToGroup: function (containerSelector, anchorId) {
        const container = document.querySelector(containerSelector);
        const anchor = document.getElementById(anchorId);
        if (!container || !anchor) return;
        const containerRect = container.getBoundingClientRect();
        const anchorRect = anchor.getBoundingClientRect();
        container.scrollLeft += anchorRect.left - containerRect.left - 180;
    }
};
