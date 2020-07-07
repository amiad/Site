import { MatSnackBar, MatDialog } from "@angular/material";
import { ToastService } from "./toast.service";
import { ResourcesService } from "./resources.service";
import { GetTextCatalogMockCreator } from "./resources.service.spec";

export class ToastServiceMockCreator {
    public toastService: ToastService;
    public resourcesService: ResourcesService;
    constructor() {
        let snackBar = { open: () => null } as any as MatSnackBar;
        let matDialog = { open: () => null } as any as MatDialog;
        this.resourcesService = new ResourcesService(new GetTextCatalogMockCreator().getTextCatalogService);
        this.toastService = new ToastService(this.resourcesService, matDialog, snackBar);
    }
}

describe("ToastService", () => {
    it("should raise toast", () => {
        let service = new ToastServiceMockCreator().toastService;

        expect(() => service.error("")).not.toThrow();
        expect(() => service.warning("")).not.toThrow();
        expect(() => service.info("")).not.toThrow();
        expect(() => service.success("")).not.toThrow();
    });
});
