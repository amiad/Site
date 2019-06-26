import { Component, Input, HostListener, OnChanges, Output, EventEmitter } from "@angular/core";

import { ResourcesService } from "../../services/resources.service";
import { SelectedRouteService } from "../../services/layers/routelayers/selected-route.service";
import { LatLngAlt } from "../../models/models";
import { BaseMapComponent } from "../base-map.component";

@Component({
    selector: "route-point-overlay",
    templateUrl: "./route-point-overlay.component.html"
})
export class RoutePointOverlayComponent extends BaseMapComponent implements OnChanges {
    public canMerge: boolean;
    public isMiddle: boolean;

    @Input()
    public latlng: LatLngAlt;

    @Input()
    private segmentIndex: number;

    @Output()
    public closed = new EventEmitter();

    public hideCoordinates: boolean;

    constructor(resources: ResourcesService,
                private readonly selectedRouteService: SelectedRouteService) {
        super(resources);
        this.canMerge = false;
        this.isMiddle = false;
        this.hideCoordinates = true;
    }

    public ngOnChanges(): void {
        this.isMiddle = this.isFirst() === false && this.isLast() === false;
        if (this.isMiddle) {
            this.canMerge = false;
            return;
        }
        this.canMerge = this.selectedRouteService.getClosestRoute(this.isFirst()) != null;
    }

    public split(): void {
        this.selectedRouteService.splitRoute(this.segmentIndex);
        this.closed.next();
    }

    public merge() {
        this.selectedRouteService.mergeRoutes(this.isFirst());
        this.closed.next();
    }

    public reverse() {
        this.selectedRouteService.reverseRoute();
        this.closed.next();
    }

    public remove = () => {
        this.selectedRouteService.removeSegment(this.segmentIndex);
        this.closed.next();
    }

    private isFirst(): boolean {
        return this.segmentIndex === 0;
    }

    private isLast(): boolean {
        return this.selectedRouteService.getSelectedRoute().segments.length - 1 === this.segmentIndex;
    }

    @HostListener("window:keydown", ["$event"])
    public onEnterPress($event: KeyboardEvent) {
        if ($event.key !== "Delete") {
            return true;
        }
        this.remove();
        return false;
    }
}
