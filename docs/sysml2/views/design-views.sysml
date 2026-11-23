// Views rendered into docs/design/generated/ for the Design document.
// Imports the NuGetCaching facade so each 'expose' statement can reference
// its target part def. Per SysML2Tools syntax, 'expose' is only valid
// inside a named 'view' usage (not a 'view def' definition), and it - not
// 'render' - is what scopes the rendered diagram content.
package NuGetCachingDesignViews {
    private import StandardViewDefinitions::*;
    private import NuGetCaching::*;

    view SoftwareStructureView : InterconnectionView {
        expose NuGetCaching::NuGetCachingSystem;
    }

    view NuGetCachingView {
        expose NuGetCachingSystem;
    }
}
