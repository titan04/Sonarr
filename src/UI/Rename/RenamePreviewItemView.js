var vent = require('../vent');
var Marionette = require('marionette');
var AsModelBoundView = require('../Mixins/AsModelBoundView');

module.exports = (function(){
    var view = Marionette.ItemView.extend({
        template      : 'Rename/RenamePreviewItemViewTemplate',
        ui            : {
            itemDiv      : '.rename-preview-item',
            checkboxIcon : '.rename-checkbox i'
        },
        onRender      : function(){
            this._setItemState();
            this.listenTo(this.model, 'change', this._setItemState);
            this.listenTo(this.model, 'rename:select', this._onRenameAll);
        },
        _setItemState : function(){
            var checked = this.model.get('rename');
            this.model.trigger('rename:select', this.model, checked);
            if(checked) {
                this.ui.itemDiv.removeClass('do-not-rename');
                this.ui.checkboxIcon.addClass('icon-check');
                this.ui.checkboxIcon.removeClass('icon-check-empty');
            }
            else {
                this.ui.itemDiv.addClass('do-not-rename');
                this.ui.checkboxIcon.addClass('icon-check-empty');
                this.ui.checkboxIcon.removeClass('icon-check');
            }
        },
        _onRenameAll  : function(model, checked){
            this.model.set('rename', checked);
        }
    });
    return AsModelBoundView.apply(view);
}).call(this);